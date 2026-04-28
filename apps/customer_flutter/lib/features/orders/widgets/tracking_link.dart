import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

import '../data/order_view_models.dart';

class TrackingLink extends StatelessWidget {
  const TrackingLink({
    super.key,
    required this.tracking,
    required this.onOpen,
  });
  final TrackingInfo tracking;
  final ValueChanged<String> onOpen;

  @override
  Widget build(BuildContext context) {
    return AppListTile(
      title: tracking.carrierName,
      subtitle: tracking.referenceNumber,
      trailing: const Icon(Icons.open_in_new),
      onTap: () => onOpen(tracking.trackingUrl),
    );
  }
}
