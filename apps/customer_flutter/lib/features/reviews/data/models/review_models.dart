import 'package:flutter/foundation.dart';

// ============================================================
// Reviews — Phase 7 customer surface (spec.md §S-7.5 .. §S-7.8)
// ============================================================
// Models parse the wire shapes spelled out in `data-model.md` §Reviews —
// customer. State values are wire strings; UI defaults to the raw value
// when unknown. Single-locale per review per Principle 4 (BR-8).

const Set<String> kKnownReviewStates = {
  'pending_moderation',
  'visible',
  'flagged',
  'hidden',
};

@immutable
class ReviewMedia {
  const ReviewMedia({required this.url, this.mediaId});
  final String url;
  final String? mediaId;

  factory ReviewMedia.fromJson(Map<String, Object?> j) => ReviewMedia(
        url: j['url'] as String? ?? '',
        mediaId: j['mediaId'] as String?,
      );
}

@immutable
class MyReviewsFilter {
  const MyReviewsFilter({
    this.state,
    this.page = 1,
    this.pageSize = 20,
  });

  final String? state;
  final int page;
  final int pageSize;

  Map<String, Object?> toQuery() => {
        if (state != null) 'state': state,
        'page': page,
        'pageSize': pageSize,
      };

  MyReviewsFilter copyWith({
    Object? state = _sentinel,
    int? page,
    int? pageSize,
  }) {
    return MyReviewsFilter(
      state: identical(state, _sentinel) ? this.state : state as String?,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}

const _sentinel = Object();

@immutable
class MyReviewListItem {
  const MyReviewListItem({
    required this.id,
    required this.productId,
    required this.productName,
    required this.rating,
    required this.state,
    required this.createdAt,
  });

  final String id;
  final String productId;
  final String productName;
  final int rating;
  final String state;
  final DateTime createdAt;

  factory MyReviewListItem.fromJson(Map<String, Object?> j) => MyReviewListItem(
        id: j['id'] as String? ?? '',
        productId: j['productId'] as String? ?? '',
        productName: j['productName'] as String? ?? '',
        rating: (j['rating'] as num?)?.toInt() ?? 0,
        state: j['state'] as String? ?? 'pending_moderation',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

@immutable
class MyReviewsPage {
  const MyReviewsPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });

  final List<MyReviewListItem> items;
  final int page;
  final int pageSize;
  final int totalCount;

  bool get hasMore => page * pageSize < totalCount;

  factory MyReviewsPage.fromJson(Map<String, Object?> j) {
    final items = j['items'];
    return MyReviewsPage(
      items: items is List
          ? items
              .whereType<Map>()
              .map(
                  (m) => MyReviewListItem.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      page: (j['page'] as num?)?.toInt() ?? 1,
      pageSize: (j['pageSize'] as num?)?.toInt() ?? 20,
      totalCount: (j['totalCount'] as num?)?.toInt() ?? 0,
    );
  }
}

@immutable
class MyReviewDetail {
  const MyReviewDetail({
    required this.id,
    required this.productId,
    required this.productName,
    required this.rating,
    required this.comment,
    required this.state,
    required this.createdAt,
    required this.media,
    required this.locale,
    this.editableUntil,
    this.moderationNote,
  });

  final String id;
  final String productId;
  final String productName;
  final int rating;
  final String comment;
  final String state;
  final DateTime createdAt;
  final List<ReviewMedia> media;
  final String locale;
  final DateTime? editableUntil;
  final String? moderationNote;

  bool isEditableAt(DateTime now) {
    final until = editableUntil;
    if (until == null) return false;
    return now.isBefore(until);
  }

  factory MyReviewDetail.fromJson(Map<String, Object?> j) {
    final media = j['media'];
    return MyReviewDetail(
      id: j['id'] as String? ?? '',
      productId: j['productId'] as String? ?? '',
      productName: j['productName'] as String? ?? '',
      rating: (j['rating'] as num?)?.toInt() ?? 0,
      comment: j['comment'] as String? ?? '',
      state: j['state'] as String? ?? 'pending_moderation',
      createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
          DateTime.now(),
      media: media is List
          ? media
              .whereType<Map>()
              .map((m) => ReviewMedia.fromJson(Map<String, Object?>.from(m)))
              .toList(growable: false)
          : const [],
      locale: j['locale'] as String? ?? 'en',
      editableUntil: j['editableUntil'] is String
          ? DateTime.tryParse(j['editableUntil']! as String)
          : null,
      moderationNote: j['moderationNote'] as String?,
    );
  }
}

@immutable
class CreateReviewRequest {
  const CreateReviewRequest({
    required this.productId,
    required this.orderId,
    required this.rating,
    required this.comment,
    required this.locale,
    this.mediaIds = const [],
  });

  final String productId;
  final String orderId;
  final int rating;
  final String comment;
  final String locale;
  final List<String> mediaIds;

  Map<String, Object?> toJson() => {
        'productId': productId,
        'orderId': orderId,
        'rating': rating,
        'comment': comment,
        'locale': locale,
        if (mediaIds.isNotEmpty) 'mediaIds': mediaIds,
      };
}

@immutable
class CreateReviewResult {
  const CreateReviewResult({
    required this.id,
    required this.state,
    required this.createdAt,
  });

  final String id;
  final String state;
  final DateTime createdAt;

  factory CreateReviewResult.fromJson(Map<String, Object?> j) =>
      CreateReviewResult(
        id: j['id'] as String? ?? '',
        state: j['state'] as String? ?? 'pending_moderation',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
      );
}

@immutable
class EditReviewRequest {
  const EditReviewRequest({
    required this.rating,
    required this.comment,
    this.mediaIds = const [],
  });

  final int rating;
  final String comment;
  final List<String> mediaIds;

  Map<String, Object?> toJson() => {
        'rating': rating,
        'comment': comment,
        if (mediaIds.isNotEmpty) 'mediaIds': mediaIds,
      };
}

@immutable
class ReportReason {
  const ReportReason({required this.key, required this.label});
  final String key;
  final String label;

  factory ReportReason.fromJson(Map<String, Object?> j) => ReportReason(
        key: j['key'] as String? ?? '',
        label: j['label'] as String? ?? '',
      );
}

@immutable
class ReportReviewRequest {
  const ReportReviewRequest({required this.reasonKey, this.note});
  final String reasonKey;
  final String? note;

  Map<String, Object?> toJson() => {
        'reasonKey': reasonKey,
        if (note != null && note!.isNotEmpty) 'note': note,
      };
}

@immutable
class ReportReviewResult {
  const ReportReviewResult({required this.id, required this.state});
  final String id;
  final String state;

  factory ReportReviewResult.fromJson(Map<String, Object?> j) =>
      ReportReviewResult(
        id: j['id'] as String? ?? '',
        state: j['state'] as String? ?? 'submitted',
      );
}
