class Category {
  const Category({required this.id, required this.name, required this.active});
  final String id;
  final String name;
  final bool active;
  factory Category.fromJson(Map<String, dynamic> json) => Category(
    id: json['id'].toString(),
    name: json['name']?.toString() ?? '',
    active: json['active'] == true,
  );
}
