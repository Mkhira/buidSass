import 'package:cached_network_image/cached_network_image.dart';
import 'package:design_system/design_system.dart' hide AppLocalizations;
import 'package:flutter/material.dart';

class MediaGallery extends StatefulWidget {
  const MediaGallery({super.key, required this.urls});
  final List<String> urls;

  @override
  State<MediaGallery> createState() => _MediaGalleryState();
}

class _MediaGalleryState extends State<MediaGallery> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    if (widget.urls.isEmpty) {
      return Container(
        height: 280,
        color: AppColors.neutral,
      );
    }
    return Column(
      children: [
        SizedBox(
          height: 280,
          child: PageView.builder(
            itemCount: widget.urls.length,
            onPageChanged: (i) => setState(() => _index = i),
            itemBuilder: (_, i) => CachedNetworkImage(
              imageUrl: widget.urls[i],
              fit: BoxFit.contain,
              placeholder: (_, __) => Container(color: AppColors.neutral),
            ),
          ),
        ),
        const SizedBox(height: AppSpacing.sm),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: List.generate(widget.urls.length, (i) {
            return Container(
              width: 8,
              height: 8,
              margin: const EdgeInsets.symmetric(horizontal: 3),
              decoration: BoxDecoration(
                color: i == _index ? AppColors.primary : AppColors.neutral,
                shape: BoxShape.circle,
              ),
            );
          }),
        ),
      ],
    );
  }
}
