class LotImage {
  const LotImage({required this.id, required this.url});
  final String id;
  final String url;
  factory LotImage.fromJson(Map<String, dynamic> json) =>
      LotImage(id: json['id'].toString(), url: json['url']?.toString() ?? '');
}

class LotItem {
  const LotItem({
    required this.id,
    required this.title,
    required this.state,
    required this.version,
    this.submittedAt,
  });
  final String id;
  final String title;
  final String state;
  final int version;
  final DateTime? submittedAt;
  factory LotItem.fromJson(Map<String, dynamic> json) => LotItem(
    id: json['id'].toString(),
    title: json['title']?.toString() ?? '',
    state: json['state']?.toString() ?? '',
    version: int.tryParse(json['version'].toString()) ?? 1,
    submittedAt: json['submittedAt'] == null
        ? null
        : DateTime.tryParse(json['submittedAt'].toString()),
  );
}

class LotDetail extends LotItem {
  LotDetail({
    required super.id,
    required super.title,
    required super.state,
    required super.version,
    required this.categoryName,
    required this.content,
    required this.images,
    super.submittedAt,
  });
  final String categoryName;
  final Map<String, dynamic> content;
  final List<LotImage> images;

  factory LotDetail.fromJson(Map<String, dynamic> json) => LotDetail(
    id: json['id'].toString(),
    title: (json['content'] as Map?)?['title']?.toString() ?? '',
    state: json['state']?.toString() ?? '',
    version: int.tryParse(json['version'].toString()) ?? 1,
    categoryName: json['categoryName']?.toString() ?? '',
    content: Map<String, dynamic>.from((json['content'] as Map?) ?? const {}),
    images: ((json['images'] as List?) ?? const [])
        .map((e) => LotImage.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList(),
    submittedAt: json['submittedAt'] == null
        ? null
        : DateTime.tryParse(json['submittedAt'].toString()),
  );
}
