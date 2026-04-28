import 'dart:async';

import 'package:app_links/app_links.dart';

/// AppLinksAdapter — wraps `app_links` for universal-link / app-link receipt.
/// `go_router` listens to [linkStream] in the composition root and pushes the
/// matching route. The first cold-start link is delivered via [getInitialLink].
class AppLinksAdapter {
  AppLinksAdapter({AppLinks? appLinks}) : _appLinks = appLinks ?? AppLinks();

  final AppLinks _appLinks;

  Future<Uri?> getInitialLink() => _appLinks.getInitialLink();

  Stream<Uri> get linkStream => _appLinks.uriLinkStream;
}
