class User {
  const User({
    required this.id,
    required this.email,
    required this.displayName,
    required this.role,
  });
  final String id;
  final String email;
  final String displayName;
  final String role;

  factory User.fromJson(Map<String, dynamic> json) => User(
    id: json['id'].toString(),
    email: json['email']?.toString() ?? '',
    displayName: json['displayName']?.toString() ?? '',
    role: json['role']?.toString() ?? '',
  );
}
