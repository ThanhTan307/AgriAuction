import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/network/api_client.dart';
import '../../core/ui/formatters.dart';
import '../catalog/category.dart';
import 'lot_models.dart';

String normalizeLotDecimal(String value) {
  var text = value.trim().replaceAll(' ', '');
  if (text.isEmpty) return text;
  final comma = text.lastIndexOf(',');
  final dot = text.lastIndexOf('.');
  if (comma >= 0 && dot >= 0) {
    final decimalSeparator = comma > dot ? ',' : '.';
    final groupingSeparator = decimalSeparator == ',' ? '.' : ',';
    text = text.replaceAll(groupingSeparator, '');
    if (decimalSeparator == ',') text = text.replaceFirst(',', '.');
    return text;
  }
  if (comma >= 0) {
    return text.replaceAll(',', '.');
  }
  if (RegExp(r'^\d{1,3}(\.\d{3})+$').hasMatch(text)) {
    return text.replaceAll('.', '');
  }
  return text;
}

class LotEditorPage extends StatefulWidget {
  const LotEditorPage({super.key, required this.api, this.existing});
  final ApiClient api;
  final LotDetail? existing;
  @override
  State<LotEditorPage> createState() => _LotEditorPageState();
}

class _LotEditorPageState extends State<LotEditorPage> {
  final _form = GlobalKey<FormState>();
  final _scrollController = ScrollController();
  final _title = TextEditingController(),
      _description = TextEditingController(),
      _location = TextEditingController(),
      _quantity = TextEditingController(),
      _weight = TextEditingController(),
      _price = TextEditingController(),
      _increment = TextEditingController(),
      _quality = TextEditingController();
  final _quantityUnit = TextEditingController(text: 'kg'),
      _weightUnit = TextEditingController(text: 'kg'),
      _currency = TextEditingController(text: 'VND'),
      _basis = TextEditingController(text: 'whole lot simulation');
  List<Category> _categories = [];
  String? _categoryId;
  DateTime _starts = DateTime.now().add(const Duration(days: 1)),
      _ends = DateTime.now().add(const Duration(days: 2));
  int _version = 1;
  String? _savedLotId;
  bool _busy = false;
  String? _error;
  Map<String, String> _fieldErrors = {};
  final List<XFile> _photos = [];

  @override
  void initState() {
    super.initState();
    final lot = widget.existing;
    if (lot != null) {
      _savedLotId = lot.id;
      final c = lot.content;
      _version = lot.version;
      _categoryId = c['categoryId']?.toString();
      _title.text = c['title']?.toString() ?? '';
      _description.text = c['description']?.toString() ?? '';
      _location.text = c['location']?.toString() ?? '';
      _quantity.text = c['quantity']?.toString() ?? '';
      _weight.text = c['weight']?.toString() ?? '';
      _price.text = c['proposedStartingPrice']?.toString() ?? '';
      _increment.text = c['proposedIncrement']?.toString() ?? '';
      _quantityUnit.text = c['quantityUnit']?.toString() ?? 'kg';
      _weightUnit.text = c['weightUnit']?.toString() ?? 'kg';
      _currency.text = c['currency']?.toString() ?? 'VND';
      _basis.text = c['priceBasis']?.toString() ?? 'per kilogram';
      _quality.text = c['qualityNotes']?.toString() ?? '';
      _starts =
          DateTime.tryParse(c['proposedStartsAt']?.toString() ?? '') ?? _starts;
      _ends = DateTime.tryParse(c['proposedEndsAt']?.toString() ?? '') ?? _ends;
    }
    _loadCategories();
  }

  Future<void> _loadCategories() async {
    try {
      final json = Map<String, dynamic>.from(
        await widget.api.get('/api/v1/catalog/categories?page=1&pageSize=100'),
      );
      if (mounted) {
        setState(
          () => _categories = ((json['items'] as List?) ?? const [])
              .map(
                (e) => Category.fromJson(Map<String, dynamic>.from(e as Map)),
              )
              .toList(),
        );
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  String? _required(String? value) =>
      value == null || value.trim().isEmpty ? 'Bắt buộc điền thông tin này.' : null;

  String? _decimal(String? value, {required int maxScale, bool allowZero = false}) {
    final required = _required(value);
    if (required != null) return required;
    final normalized = normalizeLotDecimal(value!);
    final number = decimalPattern.firstMatch(normalized);
    if (number == null) return 'Nhập số hợp lệ, ví dụ: 2000000 hoặc 2.5';
    if ((number.group(2)?.length ?? 0) > maxScale) {
      return 'Tối đa $maxScale chữ số thập phân.';
    }
    final parsed = double.tryParse(normalized);
    if (parsed == null || (!allowZero && parsed <= 0) || parsed < 0) {
      return 'Giá trị phải lớn hơn 0.';
    }
    return null;
  }

  static final decimalPattern = RegExp(r'^(\d+)(?:\.(\d+))?$');

  Map<String, dynamic> _content() => {
    'categoryId': _categoryId,
    'title': _title.text.trim(),
    'description': _description.text.trim(),
    'location': _location.text.trim(),
    'quantity': normalizeLotDecimal(_quantity.text),
    'quantityUnit': _quantityUnit.text.trim(),
    'weight': normalizeLotDecimal(_weight.text),
    'weightUnit': _weightUnit.text.trim(),
    'proposedStartingPrice': normalizeLotDecimal(_price.text),
    'proposedIncrement': normalizeLotDecimal(_increment.text),
    'currency': _currency.text.trim().toUpperCase(),
    'priceBasis': _basis.text.trim(),
    'proposedStartsAt': _starts.toUtc().toIso8601String(),
    'proposedEndsAt': _ends.toUtc().toIso8601String(),
    'qualityNotes': _quality.text.trim(),
  };

  Future<void> _save({required bool submit}) async {
    final valid = _form.currentState?.validate() ?? false;
    if (!valid || _categoryId == null) {
      setState(() {
        _error = 'Vui lòng kiểm tra và sửa các thông tin chưa hợp lệ.';
        if (_categoryId == null) {
          _fieldErrors = {'categoryId': 'Vui lòng chọn danh mục nông sản.'};
        }
      });
      if (_scrollController.hasClients) {
        _scrollController.animateTo(
          0,
          duration: const Duration(milliseconds: 300),
          curve: Curves.easeOut,
        );
      }
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
      _fieldErrors = {};
    });
    try {
      final body = {'content': _content()};
      final result = _savedLotId == null
          ? await widget.api.post('/api/v1/lots', body)
          : await widget.api.patch('/api/v1/lots/$_savedLotId', {
              ...body,
              'expectedVersion': _version,
            });
      var lot = LotDetail.fromJson(Map<String, dynamic>.from(result));
      _savedLotId = lot.id;
      _version = lot.version;
      while (_photos.isNotEmpty) {
        final photo = _photos.first;
        lot = LotDetail.fromJson(
          await widget.api.uploadImage(lot.id, photo.path, lot.version),
        );
        _version = lot.version;
        _photos.removeAt(0);
      }
      if (submit) {
        lot = LotDetail.fromJson(
          Map<String, dynamic>.from(
            await widget.api.post('/api/v1/lots/${lot.id}/submit', {
              'expectedVersion': lot.version,
            }),
          ),
        );
      }
      if (mounted) Navigator.pop(context, lot);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.message;
          _fieldErrors = e.fieldErrors;
        });
        if (_scrollController.hasClients) {
          _scrollController.animateTo(
            0,
            duration: const Duration(milliseconds: 300),
            curve: Curves.easeOut,
          );
        }
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _pick() async {
    final picked = await ImagePicker().pickMultiImage(imageQuality: 90);
    if (picked.isNotEmpty) setState(() => _photos.addAll(picked));
  }

  @override
  void dispose() {
    _scrollController.dispose();
    for (final c in [
      _title,
      _description,
      _location,
      _quantity,
      _weight,
      _price,
      _increment,
      _quality,
      _quantityUnit,
      _weightUnit,
      _currency,
      _basis,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  Widget _sectionHeader(String title, {String? subtitle}) => Padding(
    padding: const EdgeInsets.only(top: 20, bottom: 8),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: const TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.bold,
            color: Color(0xFF15803D),
            letterSpacing: 0.3,
          ),
        ),
        if (subtitle != null) ...[
          const SizedBox(height: 2),
          Text(
            subtitle,
            style: const TextStyle(fontSize: 12, color: Color(0xFF64748B)),
          ),
        ],
      ],
    ),
  );

  String? _priceHelper(TextEditingController controller) {
    final text = controller.text.trim();
    if (text.isEmpty) return null;
    final norm = normalizeLotDecimal(text);
    final val = double.tryParse(norm);
    if (val != null && val > 0) {
      return 'Xem trước: ${formatVnd(norm, _currency.text.trim().toUpperCase())}';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(widget.existing == null ? 'Tạo lô nông sản mới' : 'Chỉnh sửa lô nông sản'),
    ),
    body: Form(
      key: _form,
      child: ListView(
        controller: _scrollController,
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
        children: [
          if (_error != null)
            Container(
              margin: const EdgeInsets.only(bottom: 16),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: const Color(0xFFFEF2F2),
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: const Color(0xFFFECACA)),
              ),
              child: Row(
                children: [
                  const Icon(Icons.error_outline, size: 20, color: Color(0xFFDC2626)),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      _error!,
                      style: const TextStyle(color: Color(0xFFB91C1C), fontSize: 13),
                    ),
                  ),
                ],
              ),
            ),

          // --- 1. THÔNG TIN NÔNG SẢN ---
          _sectionHeader('1. THÔNG TIN NÔNG SẢN', subtitle: 'Phân loại và thông tin nhận diện cơ bản của lô hàng'),
          DropdownButtonFormField<String>(
            initialValue: _categoryId,
            decoration: InputDecoration(
              labelText: 'Danh mục nông sản',
              errorText: _fieldErrors['categoryId'],
            ),
            items: _categories
                .map((c) => DropdownMenuItem(value: c.id, child: Text(c.name)))
                .toList(),
            onChanged: (v) => setState(() {
              _categoryId = v;
              _fieldErrors.remove('categoryId');
            }),
            validator: (_) =>
                _fieldErrors['categoryId'] ??
                (_categoryId == null ? 'Vui lòng chọn danh mục.' : null),
          ),
          _field(_title, 'Tên sản phẩm', hint: 'Ví dụ: Sầu riêng Ri6, Thanh long ruột đỏ...'),
          _field(_description, 'Mô tả chi tiết', maxLines: 3, hint: 'Mô tả đặc điểm, quy cách thu hoạch, đóng gói...'),
          _field(_location, 'Vùng trồng / Địa điểm', hint: 'Ví dụ: Xã Tân Lập, Huyện Hàm Thuận Nam, Bình Thuận'),

          // --- 2. SẢN LƯỢNG & QUY CÁCH ---
          _sectionHeader('2. SẢN LƯỢNG & QUY CÁCH', subtitle: 'Thông tin khối lượng thực tế và đơn vị đo lường'),
          _field(
            _quantity,
            'Số lượng',
            numeric: true,
            hint: 'Ví dụ: 500, 1000',
            validator: (value) => _decimal(value, maxScale: 3),
          ),
          _field(_quantityUnit, 'Đơn vị số lượng', hint: 'Ví dụ: thùng, sọt, kiện, kg'),
          _field(
            _weight,
            'Tổng khối lượng',
            numeric: true,
            hint: 'Ví dụ: 1000, 2500',
            validator: (value) => _decimal(value, maxScale: 3),
          ),
          _field(_weightUnit, 'Đơn vị khối lượng', hint: 'Ví dụ: kg, tấn, tạ'),

          // --- 3. ĐỀ XUẤT GIÁ & LỊCH ĐẤU GIÁ ---
          _sectionHeader('3. ĐỀ XUẤT GIÁ & LỊCH ĐẤU GIÁ', subtitle: 'Mức giá đề xuất của Farmer cho phiên đấu giá mô phỏng'),
          _field(
            _price,
            'Giá khởi điểm đề xuất',
            numeric: true,
            hint: 'Ví dụ: 12000000',
            helper: _priceHelper(_price),
            validator: (value) => _decimal(value, maxScale: 4),
          ),
          _field(
            _increment,
            'Bước giá đề xuất',
            numeric: true,
            hint: 'Ví dụ: 500000',
            helper: _priceHelper(_increment),
            validator: (value) => _decimal(value, maxScale: 4),
          ),
          _field(_currency, 'Mã tiền tệ (mặc định VND)'),
          _field(_basis, 'Cơ sở tính giá'),
          _field(_quality, 'Ghi chú chất lượng nông sản', maxLines: 3, hint: 'Thông tin về chứng nhận VietGAP, độ tươi, kích cỡ trái...'),

          // --- 4. HÌNH ẢNH MINH CHỨNG ---
          _sectionHeader('4. HÌNH ẢNH MINH CHỨNG', subtitle: 'Hình ảnh thực tế giúp tăng độ tin cậy khi kiểm duyệt'),
          OutlinedButton.icon(
            onPressed: _busy ? null : _pick,
            icon: const Icon(Icons.add_photo_alternate_outlined),
            label: Text(
              _photos.isEmpty
                  ? 'Chọn ảnh sản phẩm (chọn từ thư viện)'
                  : 'Thêm ảnh (hiện đã chọn ${_photos.length} ảnh)',
            ),
          ),
          if (_photos.isNotEmpty) ...[
            const SizedBox(height: 12),
            SizedBox(
              height: 96,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: _photos.length,
                separatorBuilder: (context, index) => const SizedBox(width: 10),
                itemBuilder: (context, index) {
                  final p = _photos[index];
                  return Stack(
                    children: [
                      ClipRRect(
                        borderRadius: BorderRadius.circular(8),
                        child: Container(
                          decoration: BoxDecoration(
                            border: Border.all(color: const Color(0xFFE2E8F0)),
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: Image.file(
                            File(p.path),
                            width: 96,
                            height: 96,
                            fit: BoxFit.cover,
                          ),
                        ),
                      ),
                      Positioned(
                        top: 4,
                        right: 4,
                        child: GestureDetector(
                          onTap: () => setState(() => _photos.removeAt(index)),
                          child: Container(
                            padding: const EdgeInsets.all(4),
                            decoration: const BoxDecoration(
                              color: Color(0xCC000000),
                              shape: BoxShape.circle,
                            ),
                            child: const Icon(
                              Icons.close,
                              size: 14,
                              color: Colors.white,
                            ),
                          ),
                        ),
                      ),
                    ],
                  );
                },
              ),
            ),
          ],
          const SizedBox(height: 16),
        ],
      ),
    ),
    bottomNavigationBar: SafeArea(
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        decoration: const BoxDecoration(
          color: Colors.white,
          border: Border(top: BorderSide(color: Color(0xFFE2E8F0))),
        ),
        child: Row(
          children: [
            Expanded(
              child: OutlinedButton(
                onPressed: _busy ? null : () => _save(submit: false),
                child: const Text('Lưu nháp'),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: FilledButton(
                onPressed: _busy ? null : () => _save(submit: true),
                child: _busy
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: Colors.white,
                        ),
                      )
                    : const Text('Gửi kiểm duyệt'),
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Widget _field(
    TextEditingController controller,
    String label, {
    bool numeric = false,
    int maxLines = 1,
    String? hint,
    String? helper,
    String? Function(String?)? validator,
  }) => Padding(
    padding: const EdgeInsets.only(top: 12),
    child: TextFormField(
      controller: identical(controller, _basis) ? null : controller,
      initialValue: identical(controller, _basis)
          ? displayPriceBasis(controller.text)
          : null,
      readOnly: identical(controller, _basis),
      maxLines: maxLines,
      keyboardType: numeric
          ? const TextInputType.numberWithOptions(decimal: true)
          : (maxLines > 1 ? TextInputType.multiline : TextInputType.text),
      decoration: InputDecoration(
        labelText: label,
        hintText: hint,
        helperText: helper,
        helperStyle: const TextStyle(
          fontSize: 12,
          color: Color(0xFF15803D),
          fontWeight: FontWeight.w500,
        ),
        errorText: _fieldErrors[_fieldName(controller)],
      ),
      validator: (value) =>
          _fieldErrors[_fieldName(controller)] ??
          (validator ?? _required)(value),
    ),
  );

  String _fieldName(TextEditingController controller) {
    if (identical(controller, _title)) return 'title';
    if (identical(controller, _description)) return 'description';
    if (identical(controller, _location)) return 'location';
    if (identical(controller, _quantity)) return 'quantity';
    if (identical(controller, _quantityUnit)) return 'quantityUnit';
    if (identical(controller, _weight)) return 'weight';
    if (identical(controller, _weightUnit)) return 'weightUnit';
    if (identical(controller, _price)) return 'proposedStartingPrice';
    if (identical(controller, _increment)) return 'proposedIncrement';
    if (identical(controller, _currency)) return 'currency';
    if (identical(controller, _basis)) return 'priceBasis';
    return 'qualityNotes';
  }
}
