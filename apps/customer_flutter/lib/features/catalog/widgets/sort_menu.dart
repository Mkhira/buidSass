import 'package:flutter/material.dart';

class SortMenu extends StatelessWidget {
  const SortMenu({
    super.key,
    required this.activeKey,
    required this.options,
    required this.onSelected,
  });

  final String? activeKey;
  final Map<String, String> options; // sortKey -> labelKey
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    return PopupMenuButton<String>(
      icon: const Icon(Icons.sort),
      onSelected: onSelected,
      itemBuilder: (ctx) => options.entries
          .map((e) => PopupMenuItem<String>(
                value: e.key,
                child: Text(e.value),
              ))
          .toList(growable: false),
    );
  }
}
