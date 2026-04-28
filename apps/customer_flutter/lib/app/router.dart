import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/auth_session_bloc.dart';
import '../core/cart/anonymous_cart_token_store.dart';
import '../features/auth/bloc/login_bloc.dart';
import '../features/auth/bloc/otp_bloc.dart';
import '../features/auth/bloc/password_reset_bloc.dart';
import '../features/auth/bloc/register_bloc.dart';
import '../features/auth/data/auth_repository.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/auth/screens/otp_screen.dart';
import '../features/auth/screens/password_reset_screen.dart';
import '../features/auth/screens/register_screen.dart';
import '../features/cart/bloc/cart_bloc.dart';
import '../features/cart/data/cart_repository.dart';
import '../features/cart/screens/cart_screen.dart';
import '../features/catalog/bloc/listing_bloc.dart';
import '../features/catalog/bloc/product_detail_bloc.dart';
import '../features/catalog/data/catalog_repository.dart';
import '../features/catalog/screens/listing_screen.dart';
import '../features/catalog/screens/product_detail_screen.dart';
import '../features/checkout/bloc/checkout_bloc.dart';
import '../features/checkout/data/checkout_repository.dart';
import '../features/checkout/screens/checkout_screen.dart';
import '../features/checkout/screens/order_confirmation_screen.dart';
import '../features/home/bloc/home_bloc.dart';
import '../features/home/data/home_repository.dart';
import '../features/home/screens/home_screen.dart';
import '../features/more/bloc/addresses_bloc.dart';
import '../features/more/data/addresses_repository.dart';
import '../features/more/screens/addresses_screen.dart';
import '../features/more/screens/more_screen.dart';
import '../features/more/screens/verification_cta_screen.dart';
import '../features/orders/bloc/order_detail_bloc.dart';
import '../features/orders/bloc/order_list_bloc.dart';
import '../features/orders/data/orders_repository.dart';
import '../features/orders/screens/order_detail_screen.dart';
import '../features/orders/screens/orders_list_screen.dart';

/// Customer-app routing. Routes mirror `contracts/deeplink-routes.md`.
/// Auth-gated paths redirect through `/auth/login?continueTo=…`.
GoRouter buildRouter(AuthSessionBloc authBloc) {
  final sl = GetIt.instance;
  return GoRouter(
    initialLocation: '/',
    refreshListenable: _BlocRefresh(authBloc.stream),
    redirect: (context, gstate) {
      final auth = authBloc.state;
      final loc = gstate.matchedLocation;
      final isAuthGated = _authGatedPrefixes.any(loc.startsWith);
      final isLoginOrRegister =
          loc == '/auth/login' || loc == '/auth/register' || loc == '/auth/otp';
      final isGuest = auth is AuthGuest || auth is AuthRefreshFailed;
      if (isAuthGated && isGuest) {
        final next = Uri.encodeComponent(loc);
        return '/auth/login?continueTo=$next';
      }
      // Redirect away from login/register/otp on Authenticated only —
      // /auth/reset and /auth/verify are one-shot links that must remain
      // reachable for re-authentication / email-confirm flows.
      if (isLoginOrRegister && auth is AuthAuthenticated) {
        final continueTo = gstate.uri.queryParameters['continueTo'];
        if (continueTo != null && continueTo.isNotEmpty) {
          return Uri.decodeComponent(continueTo);
        }
        return '/';
      }
      return null;
    },
    errorBuilder: (context, state) => Scaffold(
      appBar: AppBar(),
      body: Center(child: Text(state.error?.toString() ?? '404')),
    ),
    routes: <RouteBase>[
      GoRoute(
        path: '/',
        name: 'home',
        builder: (context, _) => BlocProvider(
          create: (_) => HomeBloc(repository: sl<HomeRepository>())
            ..add(const HomeRequested()),
          child: const HomeScreen(),
        ),
      ),
      GoRoute(
        path: '/p/:productId',
        name: 'productDetail',
        builder: (context, s) {
          final productId = s.pathParameters['productId']!;
          final isVerified = (authBloc.state is AuthAuthenticated)
              ? (authBloc.state as AuthAuthenticated).isVerified
              : false;
          return BlocProvider(
            create: (_) => ProductDetailBloc(
              repository: sl<CatalogRepository>(),
              isCustomerVerified: isVerified,
            )..add(ProductRequested(productId)),
            child: ProductDetailScreen(productId: productId),
          );
        },
      ),
      GoRoute(
        path: '/c/:categoryId',
        name: 'category',
        builder: (context, s) => BlocProvider(
          create: (_) => ListingBloc(repository: sl<CatalogRepository>())
            ..add(CategorySet(s.pathParameters['categoryId'])),
          child: const ListingScreen(),
        ),
      ),
      GoRoute(
        path: '/search',
        name: 'search',
        builder: (context, s) {
          final q = s.uri.queryParameters['q'] ?? '';
          return BlocProvider(
            create: (_) => ListingBloc(repository: sl<CatalogRepository>())
              ..add(QueryChanged(q)),
            child: const ListingScreen(),
          );
        },
      ),
      GoRoute(
        path: '/cart',
        name: 'cart',
        builder: (context, _) => BlocProvider(
          create: (_) => CartBloc(
            repository: sl<CartRepository>(),
            tokenStore: sl<AnonymousCartTokenStore>(),
            authSessionBloc: authBloc,
          )..add(const CartRefreshed()),
          child: const CartScreen(),
        ),
      ),
      GoRoute(
        path: '/checkout',
        name: 'checkout',
        builder: (context, _) => BlocProvider(
          create: (_) => CheckoutBloc(repository: sl<CheckoutRepository>())
            ..add(const CheckoutStarted()),
          child: const CheckoutScreen(),
        ),
      ),
      // /checkout/drift route removed — drift is rendered inline within
      // /checkout so the bloc state survives. Deep-linking to drift
      // without prior session context would land on a stub Idle bloc
      // that can't show the drift detail anyway.
      GoRoute(
        path: '/checkout/confirmation/:orderId',
        name: 'checkoutConfirmation',
        builder: (context, s) =>
            OrderConfirmationScreen(orderId: s.pathParameters['orderId']!),
      ),
      GoRoute(
        path: '/orders',
        name: 'orders',
        builder: (context, _) => BlocProvider(
          create: (_) => OrderListBloc(repository: sl<OrdersRepository>())
            ..add(const OrderListRefreshTapped()),
          child: const OrdersListScreen(),
        ),
      ),
      GoRoute(
        path: '/o/:orderId',
        name: 'orderDetail',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => OrderDetailBloc(repository: sl<OrdersRepository>())
              ..add(OrderDetailRequested(orderId)),
            child: OrderDetailScreen(orderId: orderId),
          );
        },
      ),
      GoRoute(
        path: '/auth/login',
        name: 'login',
        builder: (context, s) => BlocProvider(
          create: (_) => LoginBloc(
            repository: sl<AuthRepository>(),
            sessionBloc: authBloc,
          ),
          child: LoginScreen(continueTo: s.uri.queryParameters['continueTo']),
        ),
      ),
      GoRoute(
        path: '/auth/register',
        name: 'register',
        builder: (context, _) => BlocProvider(
          create: (_) => RegisterBloc(
            repository: sl<AuthRepository>(),
            sessionBloc: authBloc,
          ),
          child: const RegisterScreen(),
        ),
      ),
      GoRoute(
        path: '/auth/otp',
        name: 'otp',
        builder: (context, s) {
          final challengeId = s.uri.queryParameters['challengeId'] ?? '';
          final channel = s.uri.queryParameters['channel'] ?? 'sms';
          final retry =
              int.tryParse(s.uri.queryParameters['retryAfter'] ?? '') ?? 30;
          final initial = OtpChallenge(
            challengeId: challengeId,
            channel: channel,
            retryAfterSeconds: retry,
          );
          return BlocProvider(
            create: (_) => OtpBloc(
              repository: sl<AuthRepository>(),
              sessionBloc: authBloc,
              initial: initial,
            ),
            child: const OtpScreen(),
          );
        },
      ),
      GoRoute(
        path: '/auth/reset',
        name: 'resetRequest',
        builder: (context, s) {
          final token = s.uri.queryParameters['token'];
          return BlocProvider(
            create: (_) => PasswordResetBloc(repository: sl<AuthRepository>()),
            child: (token != null && token.isNotEmpty)
                ? PasswordResetConfirmScreen(token: token)
                : const PasswordResetRequestScreen(),
          );
        },
      ),
      GoRoute(
        path: '/auth/verify',
        name: 'emailVerify',
        builder: (context, s) => Scaffold(
          appBar: AppBar(),
          body: Center(
            child: Text('verify ${s.uri.queryParameters['token'] ?? ''}'),
          ),
        ),
      ),
      GoRoute(
        path: '/more',
        name: 'more',
        builder: (context, _) => const MoreScreen(),
      ),
      GoRoute(
        path: '/more/addresses',
        name: 'addresses',
        builder: (context, _) => BlocProvider(
          create: (_) => AddressesBloc(repository: sl<AddressesRepository>())
            ..add(const AddressesRequested()),
          child: const AddressesScreen(),
        ),
      ),
      GoRoute(
        path: '/more/verification',
        name: 'verification',
        builder: (context, _) => const VerificationCtaScreen(),
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
