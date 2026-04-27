import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/auth_session_bloc.dart';

/// Customer-app routing. Routes mirror `contracts/deeplink-routes.md`.
/// Auth-gated paths redirect through `/auth/login?continueTo=…`.
GoRouter buildRouter(AuthSessionBloc authBloc) {
  return GoRouter(
    initialLocation: '/',
    refreshListenable: _BlocRefresh(authBloc.stream),
    redirect: (context, gstate) {
      final auth = authBloc.state;
      final loc = gstate.matchedLocation;
      final isAuthGated = _authGatedPrefixes.any(loc.startsWith);
      final isOnAuthScreen = loc.startsWith('/auth/');
      final isGuest = auth is AuthGuest || auth is AuthRefreshFailed;
      if (isAuthGated && isGuest) {
        final next = Uri.encodeComponent(loc);
        return '/auth/login?continueTo=$next';
      }
      if (isOnAuthScreen && auth is AuthAuthenticated) {
        final continueTo = gstate.uri.queryParameters['continueTo'];
        if (continueTo != null && continueTo.isNotEmpty) {
          return Uri.decodeComponent(continueTo);
        }
        return '/';
      }
      return null;
    },
    routes: <RouteBase>[
      GoRoute(
        path: '/',
        name: 'home',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'home'),
      ),
      GoRoute(
        path: '/p/:productId',
        name: 'productDetail',
        builder: (_, s) => _PlaceholderScreen(
          routeName: 'product:${s.pathParameters['productId']}',
        ),
      ),
      GoRoute(
        path: '/c/:categoryId',
        name: 'category',
        builder: (_, s) => _PlaceholderScreen(
          routeName: 'category:${s.pathParameters['categoryId']}',
        ),
      ),
      GoRoute(
        path: '/search',
        name: 'search',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'search'),
      ),
      GoRoute(
        path: '/cart',
        name: 'cart',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'cart'),
      ),
      GoRoute(
        path: '/checkout',
        name: 'checkout',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'checkout'),
      ),
      GoRoute(
        path: '/orders',
        name: 'orders',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'orders'),
      ),
      GoRoute(
        path: '/o/:orderId',
        name: 'orderDetail',
        builder: (_, s) => _PlaceholderScreen(
          routeName: 'order:${s.pathParameters['orderId']}',
        ),
      ),
      GoRoute(
        path: '/auth/login',
        name: 'login',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'login'),
      ),
      GoRoute(
        path: '/auth/register',
        name: 'register',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'register'),
      ),
      GoRoute(
        path: '/auth/otp',
        name: 'otp',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'otp'),
      ),
      GoRoute(
        path: '/auth/reset',
        name: 'resetConfirm',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'reset'),
      ),
      GoRoute(
        path: '/auth/verify',
        name: 'emailVerify',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'verify'),
      ),
      GoRoute(
        path: '/more',
        name: 'more',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'more'),
      ),
      GoRoute(
        path: '/more/addresses',
        name: 'addresses',
        builder: (_, __) => const _PlaceholderScreen(routeName: 'addresses'),
      ),
      GoRoute(
        path: '/more/verification',
        name: 'verification',
        builder: (_, __) =>
            const _PlaceholderScreen(routeName: 'verification'),
      ),
    ],
  );
}

const _authGatedPrefixes = <String>[
  '/checkout',
  '/orders',
  '/o/',
  '/more',
];

class _BlocRefresh extends ChangeNotifier {
  _BlocRefresh(Stream<dynamic> stream) {
    _sub = stream.listen((_) => notifyListeners());
  }

  late final StreamSubscription<dynamic> _sub;

  @override
  void dispose() {
    _sub.cancel();
    super.dispose();
  }
}

/// Placeholder rendered for every route until per-feature screens land in
/// Phases 3–6. Carries the route name so the auth-resume + deep-link wiring
/// can be smoke-tested without per-feature code.
class _PlaceholderScreen extends StatelessWidget {
  const _PlaceholderScreen({required this.routeName});
  final String routeName;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(routeName)),
      body: Center(
        child: BlocBuilder<AuthSessionBloc, AuthSessionState>(
          builder: (context, auth) {
            final label = switch (auth) {
              AuthGuest() => 'guest',
              AuthAuthenticating() => 'authenticating',
              AuthAuthenticated() => 'authenticated',
              AuthRefreshing() => 'refreshing',
              AuthRefreshFailed() => 'refresh-failed',
              AuthLoggingOut() => 'logging-out',
            };
            return Text('$routeName · $label');
          },
        ),
      ),
    );
  }
}
