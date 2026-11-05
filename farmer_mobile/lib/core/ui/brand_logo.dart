import 'package:flutter/material.dart';

class BrandLogo extends StatelessWidget {
  const BrandLogo({super.key, this.markOnly = false, this.width});

  final bool markOnly;
  final double? width;

  @override
  Widget build(BuildContext context) => const SizedBox.shrink();
}
