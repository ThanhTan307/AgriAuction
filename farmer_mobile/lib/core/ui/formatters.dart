String formatVnd(dynamic value, [String? currency]) {
  if (value == null) return '—';
  final str = value.toString().trim();
  final number = double.tryParse(str.replaceAll(',', ''));
  if (number == null || !number.isFinite) return '—';
  final integerPart = number.round().toString();
  final formattedInt = integerPart.replaceAllMapped(
    RegExp(r'(\d{1,3})(?=(\d{3})+(?!\d))'),
    (Match m) => '${m[1]}.',
  );
  final unit = currency == null || currency == 'VND' ? 'VND' : currency;
  return '$formattedInt $unit';
}

String displayPriceBasis(dynamic value) {
  switch (value?.toString()) {
    case 'whole lot simulation':
      return 'Giá áp dụng cho toàn bộ lô hàng';
    case 'per kilogram':
      return 'Giá áp dụng theo kg';
    default:
      return value?.toString() ?? '—';
  }
}

String formatDateTime(dynamic value) {
  if (value == null) return '—';
  final dt = value is DateTime ? value : DateTime.tryParse(value.toString());
  if (dt == null) return '—';
  final vietnamTime = dt.toUtc().add(const Duration(hours: 7));
  final day = vietnamTime.day.toString().padLeft(2, '0');
  final month = vietnamTime.month.toString().padLeft(2, '0');
  final year = vietnamTime.year.toString();
  final hour = vietnamTime.hour.toString().padLeft(2, '0');
  final minute = vietnamTime.minute.toString().padLeft(2, '0');
  return '$day/$month/$year $hour:$minute';
}

