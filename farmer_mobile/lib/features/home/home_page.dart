import 'package:flutter/material.dart';

import '../../core/network/api_client.dart';
import '../auth/auth_state.dart';
import '../catalog/categories_page.dart';
import '../lots/lots_page.dart';
import '../profile/profile_page.dart';
import '../activity/activity_page.dart';

class HomePage extends StatefulWidget {
  const HomePage({super.key, required this.api, required this.authState});
  final ApiClient api;
  final AuthState authState;
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  int _index = 0;
  @override
  Widget build(BuildContext context) {
    final pages = [
      LotsPage(api: widget.api),
      CategoriesPage(api: widget.api),
      ActivityPage(api: widget.api),
      ProfilePage(api: widget.api, authState: widget.authState),
    ];
    return Scaffold(
      appBar: AppBar(
        title: const Text('AgriAuction Farmer'),
      ),
      body: pages[_index],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.inventory_2_outlined),
            selectedIcon: Icon(Icons.inventory_2),
            label: 'Lô hàng',
          ),
          NavigationDestination(
            icon: Icon(Icons.category_outlined),
            selectedIcon: Icon(Icons.category),
            label: 'Danh mục',
          ),
          NavigationDestination(
            icon: Icon(Icons.view_list_outlined),
            selectedIcon: Icon(Icons.view_list),
            label: 'Hoạt động',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Tài khoản',
          ),
        ],
      ),
    );
  }
}
