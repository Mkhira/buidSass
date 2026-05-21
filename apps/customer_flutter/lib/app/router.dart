import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:get_it/get_it.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/auth_session_bloc.dart';
import '../core/localization/locale_bloc.dart';
import '../core/market/market_resolver.dart';
import '../features/auth/bloc/login_bloc.dart';
import '../features/auth/bloc/otp_bloc.dart';
import '../features/auth/bloc/password_reset_bloc.dart';
import '../features/auth/bloc/register_bloc.dart';
import '../features/auth/data/auth_repository.dart';
import '../features/auth/screens/login_screen.dart';
import '../features/auth/screens/otp_screen.dart';
import '../features/auth/screens/password_reset_screen.dart';
import '../features/auth/screens/register_screen.dart';
import '../features/b2b/bloc/awaiting_approval_bloc.dart';
import '../features/b2b/bloc/branches_bloc.dart';
import '../features/b2b/bloc/company_profile_bloc.dart';
import '../features/b2b/bloc/company_register_bloc.dart';
import '../features/b2b/bloc/invitation_accept_bloc.dart';
import '../features/b2b/bloc/invite_user_bloc.dart';
import '../features/b2b/bloc/legacy_quotation_detail_bloc.dart';
import '../features/b2b/bloc/legacy_quotations_list_bloc.dart';
import '../features/b2b/bloc/memberships_bloc.dart';
import '../features/b2b/bloc/my_quotes_bloc.dart';
import '../features/b2b/bloc/quote_detail_bloc.dart';
import '../features/b2b/bloc/quote_document_bloc.dart';
import '../features/b2b/bloc/quote_from_cart_bloc.dart';
import '../features/b2b/bloc/quote_from_product_bloc.dart';
import '../features/b2b/data/companies_gateway.dart';
import '../features/b2b/data/legacy_quotations_gateway.dart';
import '../features/b2b/data/quotes_gateway.dart';
import '../features/b2b/screens/awaiting_approval_screen.dart';
import '../features/b2b/screens/branches_screen.dart';
import '../features/b2b/screens/company_profile_screen.dart';
import '../features/b2b/screens/company_register_screen.dart';
import '../features/b2b/screens/invitation_accept_screen.dart';
import '../features/b2b/screens/invite_user_screen.dart';
import '../features/b2b/screens/legacy_quotation_detail_screen.dart';
import '../features/b2b/screens/legacy_quotations_list_screen.dart';
import '../features/b2b/screens/memberships_screen.dart';
import '../features/b2b/screens/my_quotes_screen.dart';
import '../features/b2b/screens/quote_detail_screen.dart';
import '../features/b2b/screens/quote_document_screen.dart';
import '../features/b2b/screens/quote_from_cart_screen.dart';
import '../features/b2b/screens/quote_from_product_screen.dart';
import '../features/cart/bloc/cart_v2_bloc.dart';
import '../features/cart/data/cart_store.dart';
import '../features/cart/screens/cart_screen_v2.dart';
import '../features/catalog/bloc/listing_bloc.dart';
import '../features/catalog/bloc/product_detail_bloc.dart';
import '../features/catalog/data/catalog_repository.dart';
import '../features/catalog/screens/listing_screen.dart';
import '../features/catalog/screens/product_detail_screen.dart';
import '../features/checkout/bloc/checkout_address_bloc.dart';
import '../features/checkout/bloc/checkout_payment_bloc.dart';
import '../features/checkout/bloc/checkout_review_bloc.dart';
import '../features/checkout/bloc/checkout_shipping_bloc.dart';
import '../features/checkout/bloc/checkout_start_bloc.dart';
import '../features/checkout/bloc/checkout_summary_bloc.dart';
import '../features/checkout/data/checkout_gateway.dart';
import '../features/checkout/data/models/checkout_models.dart';
import '../features/checkout/screens/address_step_screen.dart';
import '../features/checkout/screens/checkout_start_screen.dart';
import '../features/checkout/screens/checkout_summary_screen.dart';
import '../features/checkout/screens/order_confirmation_screen.dart';
import '../features/checkout/screens/payment_step_screen.dart';
import '../features/checkout/screens/review_screen.dart';
import '../features/checkout/screens/shipping_step_screen.dart';
import '../features/home/bloc/home_bloc.dart';
import '../features/home/data/home_repository.dart';
import '../features/home/screens/home_screen.dart';
import '../features/invoices/bloc/invoice_pdf_bloc.dart';
import '../features/invoices/bloc/invoice_preview_bloc.dart';
import '../features/invoices/data/invoices_gateway.dart';
import '../features/invoices/screens/invoice_pdf_screen.dart';
import '../features/invoices/screens/invoice_preview_screen.dart';
import '../features/more/bloc/addresses_bloc.dart';
import '../features/more/data/addresses_repository.dart';
import '../features/more/screens/addresses_screen.dart';
import '../features/more/screens/more_screen.dart';
import '../features/more/screens/verification_cta_screen.dart';
import '../features/orders/bloc/cancel_order_bloc.dart';
import '../features/orders/bloc/order_detail_v2_bloc.dart';
import '../features/orders/bloc/orders_list_v2_bloc.dart';
import '../features/orders/bloc/reorder_bloc.dart';
import '../features/orders/data/orders_gateway.dart';
import '../features/orders/screens/cancel_order_screen.dart';
import '../features/orders/screens/order_detail_v2_screen.dart';
import '../features/orders/screens/orders_list_v2_screen.dart';
import '../features/orders/screens/reorder_screen.dart';
import '../features/returns/bloc/return_detail_bloc.dart';
import '../features/returns/bloc/return_wizard_bloc.dart';
import '../features/returns/bloc/returns_list_bloc.dart';
import '../features/returns/data/returns_gateway.dart';
import '../features/returns/screens/return_detail_screen.dart';
import '../features/returns/screens/return_wizard_screen.dart';
import '../features/returns/screens/returns_list_screen.dart';
import '../features/reviews/bloc/my_review_detail_bloc.dart';
import '../features/reviews/bloc/my_reviews_bloc.dart';
import '../features/reviews/bloc/report_review_bloc.dart';
import '../features/reviews/bloc/review_submit_bloc.dart';
import '../features/reviews/data/reviews_customer_gateway.dart';
import '../features/reviews/screens/my_review_detail_screen.dart';
import '../features/reviews/screens/my_reviews_screen.dart';
import '../features/reviews/screens/report_review_screen.dart';
import '../features/reviews/screens/review_submit_screen.dart';
import '../features/search/bloc/lookup_bloc.dart';
import '../features/search/bloc/search_bloc.dart';
import '../features/search/data/recent_searches_store.dart';
import '../features/search/data/search_gateway.dart';
import '../features/search/screens/lookup_screen.dart';
import '../features/search/screens/search_screen.dart';
import '../features/verification/bloc/renew_bloc.dart';
import '../features/verification/bloc/resubmit_cubit.dart';
import '../features/verification/bloc/verification_detail_bloc.dart';
import '../features/verification/bloc/verification_list_bloc.dart';
import '../features/verification/bloc/verification_submit_bloc.dart';
import '../features/verification/data/verification_gateway.dart';
import '../features/verification/screens/renew_screen.dart';
import '../features/verification/screens/resubmit_screen.dart';
import '../features/verification/screens/verification_detail_screen.dart';
import '../features/verification/screens/verification_list_screen.dart';
import '../features/verification/screens/verification_submit_screen.dart';

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
          final q = s.uri.queryParameters['q'];
          return BlocProvider(
            create: (_) => SearchBloc(
              gateway: sl<SearchGateway>(),
              recentStore: sl<RecentSearchesStore>(),
              marketProvider: () => sl<MarketResolver>().resolve().code,
              localeProvider: () => sl<LocaleBloc>().state.locale.code,
            ),
            child: SearchScreen(initialQuery: q),
          );
        },
      ),
      GoRoute(
        path: '/search/lookup',
        name: 'searchLookup',
        builder: (context, _) => BlocProvider(
          create: (_) => LookupBloc(
            gateway: sl<SearchGateway>(),
            marketProvider: () => sl<MarketResolver>().resolve().code,
          ),
          child: const LookupScreen(),
        ),
      ),
      GoRoute(
        path: '/cart',
        name: 'cart',
        builder: (context, _) => BlocProvider(
          create: (_) => CartV2Bloc(
            store: sl<CartStore>(),
            gateway: sl<CheckoutGateway>(),
            marketProvider: () => sl<MarketResolver>().resolve().code,
          )..add(const CartStarted()),
          child: const CartScreenV2(),
        ),
      ),
      GoRoute(
        path: '/checkout',
        name: 'checkout',
        builder: (context, _) {
          final cart = sl<CartStore>().snapshot;
          return BlocProvider(
            create: (_) => CheckoutStartBloc(gateway: sl<CheckoutGateway>())
              ..add(StartCheckoutRequested(
                request: CreateSessionRequest(
                  lines: cart.lines
                      .map((l) =>
                          CreateSessionLine(productId: l.productId, qty: l.qty))
                      .toList(growable: false),
                  couponCode: cart.couponCode,
                  buyerKind: 'consumer',
                  marketCode: sl<MarketResolver>().resolve().code,
                ),
              )),
            child: const CheckoutStartScreen(),
          );
        },
      ),
      GoRoute(
        path: '/checkout/:sessionId/summary',
        name: 'checkoutSummary',
        builder: (context, s) {
          final id = s.pathParameters['sessionId']!;
          return BlocProvider(
            create: (_) => CheckoutSummaryBloc(
              gateway: sl<CheckoutGateway>(),
              sessionId: id,
            )..add(const CheckoutSummaryRequested()),
            child: CheckoutSummaryScreen(sessionId: id),
          );
        },
      ),
      GoRoute(
        path: '/checkout/:sessionId/address',
        name: 'checkoutAddress',
        builder: (context, s) {
          final id = s.pathParameters['sessionId']!;
          return BlocProvider(
            create: (_) => CheckoutAddressBloc(
                gateway: sl<CheckoutGateway>(), sessionId: id),
            child: AddressStepScreen(sessionId: id),
          );
        },
      ),
      GoRoute(
        path: '/checkout/:sessionId/shipping',
        name: 'checkoutShipping',
        builder: (context, s) {
          final id = s.pathParameters['sessionId']!;
          return BlocProvider(
            create: (_) => CheckoutShippingBloc(
              gateway: sl<CheckoutGateway>(),
              sessionId: id,
            )..add(const ShippingQuotesRequested()),
            child: ShippingStepScreen(sessionId: id),
          );
        },
      ),
      GoRoute(
        path: '/checkout/:sessionId/payment',
        name: 'checkoutPayment',
        builder: (context, s) {
          final id = s.pathParameters['sessionId']!;
          // The payment screen needs the latest summary to know the
          // server-driven `availableMethods` list (BR-5). We fetch it
          // inline via a tiny SummaryBloc and pipe the result into the
          // PaymentBloc. The screen renders nothing until both arrive.
          return _CheckoutPaymentRoute(sessionId: id);
        },
      ),
      GoRoute(
        path: '/checkout/:sessionId/review',
        name: 'checkoutReview',
        builder: (context, s) {
          final id = s.pathParameters['sessionId']!;
          return _CheckoutReviewRoute(sessionId: id);
        },
      ),
      GoRoute(
        path: '/checkout/confirmation/:orderId',
        name: 'checkoutConfirmation',
        builder: (context, s) {
          final extra = s.extra;
          return OrderConfirmationScreen(
            orderId: s.pathParameters['orderId']!,
            result: extra is SubmitResult ? extra : null,
            cartStore: sl<CartStore>(),
          );
        },
      ),
      GoRoute(
        path: '/orders',
        name: 'orders',
        builder: (context, _) => BlocProvider(
          create: (_) => OrdersListV2Bloc(gateway: sl<OrdersGateway>())
            ..add(const OrdersListStarted()),
          child: const OrdersListV2Screen(),
        ),
      ),
      GoRoute(
        path: '/o/:orderId',
        name: 'orderDetail',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => OrderDetailV2Bloc(
              gateway: sl<OrdersGateway>(),
              orderId: orderId,
            )..add(const OrderDetailStarted()),
            child: OrderDetailV2Screen(orderId: orderId),
          );
        },
      ),
      GoRoute(
        path: '/o/:orderId/cancel',
        name: 'orderCancel',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => CancelOrderBloc(
              gateway: sl<OrdersGateway>(),
              orderId: orderId,
            ),
            child: CancelOrderScreen(orderId: orderId),
          );
        },
      ),
      GoRoute(
        path: '/o/:orderId/reorder',
        name: 'orderReorder',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => ReorderBloc(
              gateway: sl<OrdersGateway>(),
              cartStore: sl<CartStore>(),
              orderId: orderId,
            )..add(const ReorderStarted()),
            child: ReorderScreen(orderId: orderId),
          );
        },
      ),
      // Phase 6 — return wizard. Path matches the order-detail
      // "Request return" CTA that has shipped since Phase 5; the
      // placeholder body is now retired.
      GoRoute(
        path: '/o/:orderId/return',
        name: 'orderReturn',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => ReturnWizardBloc(
              ordersGateway: sl<OrdersGateway>(),
              returnsGateway: sl<ReturnsGateway>(),
              orderId: orderId,
            )..add(ReturnWizardStarted(orderId)),
            child: ReturnWizardScreen(orderId: orderId),
          );
        },
      ),
      GoRoute(
        path: '/returns',
        name: 'returns',
        builder: (context, _) => BlocProvider(
          create: (_) => ReturnsListBloc(gateway: sl<ReturnsGateway>())
            ..add(const ReturnsListStarted()),
          child: const ReturnsListScreen(),
        ),
      ),
      GoRoute(
        path: '/returns/:id',
        name: 'returnDetail',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => ReturnDetailBloc(
              gateway: sl<ReturnsGateway>(),
              returnId: id,
            )..add(const ReturnDetailStarted()),
            child: ReturnDetailScreen(returnId: id),
          );
        },
      ),
      GoRoute(
        path: '/o/:orderId/invoice',
        name: 'orderInvoice',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => InvoicePreviewBloc(
              gateway: sl<InvoicesGateway>(),
              orderId: orderId,
            )..add(const InvoicePreviewStarted()),
            child: InvoicePreviewScreen(orderId: orderId),
          );
        },
      ),
      GoRoute(
        path: '/o/:orderId/invoice/pdf',
        name: 'orderInvoicePdf',
        builder: (context, s) {
          final orderId = s.pathParameters['orderId']!;
          return BlocProvider(
            create: (_) => InvoicePdfBloc(
              gateway: sl<InvoicesGateway>(),
              orderId: orderId,
            )..add(const InvoicePdfDownloadRequested()),
            child: InvoicePdfScreen(orderId: orderId),
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
        name: 'verificationCta',
        builder: (context, _) => const VerificationCtaScreen(),
      ),
      // Phase 7 — verification list. Path matches the spec's S-7.1
      // route. The legacy `/more/verification` CTA stays alive until
      // the More hub link migrates over.
      GoRoute(
        path: '/verification',
        name: 'verification',
        builder: (context, _) => BlocProvider(
          create: (_) =>
              VerificationListBloc(gateway: sl<VerificationGateway>())
                ..add(const VerificationListStarted()),
          child: const VerificationListScreen(),
        ),
      ),
      // S-7.2 submit. Idempotency-Key generated on bloc construction
      // and reused across submit retries (BR-2). Re-entering the route
      // constructs a new bloc, which generates a fresh key.
      GoRoute(
        path: '/verification/new',
        name: 'verificationNew',
        builder: (context, _) => BlocProvider(
          create: (_) => VerificationSubmitBloc(
            gateway: sl<VerificationGateway>(),
          )..add(VerificationSubmitStarted(
              marketCode: sl<MarketResolver>().resolve().code,
            )),
          child: const VerificationSubmitScreen(),
        ),
      ),
      // S-7.4 renew. Static path declared BEFORE the dynamic `:id`
      // route so go_router matches it first. Reads the prior case id
      // from the `prior` query param when arriving via the detail
      // screen's Renew CTA.
      GoRoute(
        path: '/verification/renew',
        name: 'verificationRenew',
        builder: (context, s) {
          final priorId = s.uri.queryParameters['prior'] ?? '';
          final marketCode = sl<MarketResolver>().resolve().code;
          return BlocProvider(
            create: (_) => RenewBloc(gateway: sl<VerificationGateway>())
              ..add(RenewStarted(
                priorVerificationId: priorId,
                marketCode: marketCode,
              )),
            child: const RenewScreen(),
          );
        },
      ),
      // S-7.4 resubmit. Nested under the dynamic `:id` parent so the
      // detail screen's Resubmit CTA can deep-link directly.
      GoRoute(
        path: '/verification/:id/resubmit',
        name: 'verificationResubmit',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => ResubmitCubit(
              gateway: sl<VerificationGateway>(),
              verificationId: id,
            )..load(),
            child: ResubmitScreen(verificationId: id),
          );
        },
      ),
      // S-7.3 detail + doc upload. Path :id parameter doubles as the
      // bloc's verificationId so the URL is bookmarkable/deeplinkable.
      GoRoute(
        path: '/verification/:id',
        name: 'verificationDetail',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => VerificationDetailBloc(
              gateway: sl<VerificationGateway>(),
              verificationId: id,
            )..add(const VerificationDetailStarted()),
            child: VerificationDetailScreen(verificationId: id),
          );
        },
      ),
      // S-7.5 review submit. productId + orderId from query params so
      // the order-detail "Write review" CTA can deep-link directly.
      GoRoute(
        path: '/reviews/new',
        name: 'reviewSubmit',
        builder: (context, s) {
          final productId = s.uri.queryParameters['productId'] ?? '';
          final orderId = s.uri.queryParameters['orderId'] ?? '';
          final locale = sl<LocaleBloc>().state.locale.code;
          return BlocProvider(
            create: (_) => ReviewSubmitBloc(
              gateway: sl<ReviewsCustomerGateway>(),
            )..add(ReviewSubmitStarted(
                productId: productId,
                orderId: orderId,
                locale: locale,
              )),
            child: ReviewSubmitScreen(
              productId: productId,
              orderId: orderId,
            ),
          );
        },
      ),
      // S-7.8 report someone else's review.
      GoRoute(
        path: '/reviews/:id/report',
        name: 'reviewReport',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => ReportReviewBloc(
              gateway: sl<ReviewsCustomerGateway>(),
              reviewId: id,
            )..add(const ReportReviewStarted()),
            child: const ReportReviewScreen(),
          );
        },
      ),
      // S-7.6 my reviews list.
      GoRoute(
        path: '/my-reviews',
        name: 'myReviews',
        builder: (context, _) => BlocProvider(
          create: (_) => MyReviewsBloc(gateway: sl<ReviewsCustomerGateway>())
            ..add(const MyReviewsStarted()),
          child: const MyReviewsScreen(),
        ),
      ),
      // S-7.7 my review detail / edit.
      GoRoute(
        path: '/my-reviews/:id',
        name: 'myReviewDetail',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => MyReviewDetailBloc(
              gateway: sl<ReviewsCustomerGateway>(),
              reviewId: id,
            )..add(const MyReviewDetailStarted()),
            child: const MyReviewDetailScreen(),
          );
        },
      ),
      // Phase 8 — b2b. Quote-side first (S-8.1..S-8.6). The static
      // /quotes/awaiting-approval + /quotes/from-cart routes come BEFORE
      // /quotes/:id so go_router matches them first.
      GoRoute(
        path: '/quotes',
        name: 'quotes',
        builder: (context, _) => BlocProvider(
          create: (_) => MyQuotesBloc(gateway: sl<QuotesGateway>())
            ..add(const MyQuotesStarted()),
          child: const MyQuotesScreen(),
        ),
      ),
      GoRoute(
        path: '/quotes/awaiting-approval',
        name: 'quotesAwaiting',
        builder: (context, _) => BlocProvider(
          create: (_) => AwaitingApprovalBloc(gateway: sl<QuotesGateway>())
            ..add(const AwaitingApprovalStarted()),
          child: const AwaitingApprovalScreen(),
        ),
      ),
      GoRoute(
        path: '/quotes/from-cart',
        name: 'quoteFromCart',
        builder: (context, _) => BlocProvider(
          create: (_) => QuoteFromCartBloc(
            gateway: sl<QuotesGateway>(),
            cartStore: sl<CartStore>(),
          )..add(const QuoteFromCartStarted()),
          child: const QuoteFromCartScreen(),
        ),
      ),
      GoRoute(
        path: '/products/:slug/quote',
        name: 'quoteFromProduct',
        builder: (context, s) {
          final productId = s.pathParameters['slug'] ?? '';
          return BlocProvider(
            create: (_) => QuoteFromProductBloc(gateway: sl<QuotesGateway>())
              ..add(QuoteFromProductStarted(productId: productId)),
            child: QuoteFromProductScreen(productId: productId),
          );
        },
      ),
      // Document deep-link comes before /quotes/:id since its prefix is
      // /quotes/:id/versions/...
      GoRoute(
        path: '/quotes/:quoteId/versions/:versionId/document',
        name: 'quoteDocument',
        builder: (context, s) {
          final quoteId = s.pathParameters['quoteId']!;
          final versionId = s.pathParameters['versionId']!;
          final locale = s.uri.queryParameters['locale'] ??
              sl<LocaleBloc>().state.locale.code;
          return BlocProvider(
            create: (_) => QuoteDocumentBloc(gateway: sl<QuotesGateway>())
              ..add(QuoteDocumentDownloadRequested(
                quoteId: quoteId,
                versionId: versionId,
                locale: locale,
              )),
            child: QuoteDocumentScreen(
              quoteId: quoteId,
              versionId: versionId,
              initialLocale: locale,
            ),
          );
        },
      ),
      GoRoute(
        path: '/quotes/:id',
        name: 'quoteDetail',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) =>
                QuoteDetailBloc(gateway: sl<QuotesGateway>(), quoteId: id)
                  ..add(const QuoteDetailStarted()),
            child: QuoteDetailScreen(quoteId: id),
          );
        },
      ),
      // S-8.7 — company register. Static path; before `/company/:id`.
      GoRoute(
        path: '/company/register',
        name: 'companyRegister',
        builder: (context, _) => BlocProvider(
          create: (_) => CompanyRegisterBloc(gateway: sl<CompaniesGateway>())
            ..add(CompanyRegisterStarted(
              marketCode: sl<MarketResolver>().resolve().code,
            )),
          child: const CompanyRegisterScreen(),
        ),
      ),
      // S-8.9 / S-8.10 / S-8.12 — nested under /company/:id.
      GoRoute(
        path: '/company/:id/branches',
        name: 'companyBranches',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => BranchesBloc(
              gateway: sl<CompaniesGateway>(),
              companyId: id,
            )..add(const BranchesStarted()),
            child: BranchesScreen(companyId: id),
          );
        },
      ),
      GoRoute(
        path: '/company/:id/invitations/new',
        name: 'companyInvite',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => InviteUserBloc(
              gateway: sl<CompaniesGateway>(),
              companyId: id,
            ),
            child: const InviteUserScreen(),
          );
        },
      ),
      GoRoute(
        path: '/company/:id/members',
        name: 'companyMembers',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => MembershipsBloc(
              gateway: sl<CompaniesGateway>(),
              companyId: id,
            )..add(const MembershipsStarted()),
            child: MembershipsScreen(companyId: id),
          );
        },
      ),
      // S-8.8 — company profile. Dynamic catchall; declared last.
      GoRoute(
        path: '/company/:id',
        name: 'companyProfile',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => CompanyProfileBloc(
              gateway: sl<CompaniesGateway>(),
              companyId: id,
            )..add(const CompanyProfileStarted()),
            child: CompanyProfileScreen(companyId: id),
          );
        },
      ),
      // S-8.11 — invitation accept deep link.
      GoRoute(
        path: '/invitations/:token',
        name: 'invitationAccept',
        builder: (context, s) {
          final token = s.pathParameters['token'] ?? '';
          return BlocProvider(
            create: (_) => InvitationAcceptBloc(
              gateway: sl<CompaniesGateway>(),
              token: token,
            )..add(const InvitationAcceptStarted()),
            child: const InvitationAcceptScreen(),
          );
        },
      ),
      // S-8.legacy.1 — legacy quotations list. Menu entry surfaces this
      // route only when the list isn't empty; the bloc transitions to
      // Empty when the gateway returns no items (404 → []).
      GoRoute(
        path: '/legacy-quotations',
        name: 'legacyQuotations',
        builder: (context, _) => BlocProvider(
          create: (_) => LegacyQuotationsListBloc(
            gateway: sl<LegacyQuotationsGateway>(),
          )..add(const LegacyQuotationsListStarted()),
          child: const LegacyQuotationsListScreen(),
        ),
      ),
      // S-8.legacy.2 — legacy quotation detail / accept / reject.
      GoRoute(
        path: '/legacy-quotations/:id',
        name: 'legacyQuotationDetail',
        builder: (context, s) {
          final id = s.pathParameters['id']!;
          return BlocProvider(
            create: (_) => LegacyQuotationDetailBloc(
              gateway: sl<LegacyQuotationsGateway>(),
              quotationId: id,
            )..add(const LegacyQuotationDetailStarted()),
            child: LegacyQuotationDetailScreen(quotationId: id),
          );
        },
      ),
    ],
  );
}

const _authGatedPrefixes = <String>[
  '/checkout',
  '/orders',
  '/o/',
  '/more',
  '/returns',
  '/verification',
  '/reviews',
  '/my-reviews',
  '/quotes',
  '/company',
  '/legacy-quotations',
  '/invitations',
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

/// Payment-step route helper. Fetches the latest summary so the bloc is
/// constructed against the server-driven `availableMethods` list (BR-5)
/// before the user lands on the screen. Caches the future in
/// [initState] so rebuilds (keyboard, route animation frames) don't
/// re-issue `getSummary`.
class _CheckoutPaymentRoute extends StatefulWidget {
  const _CheckoutPaymentRoute({required this.sessionId});
  final String sessionId;

  @override
  State<_CheckoutPaymentRoute> createState() => _CheckoutPaymentRouteState();
}

class _CheckoutPaymentRouteState extends State<_CheckoutPaymentRoute> {
  late final Future<CheckoutSummary> _summary;

  @override
  void initState() {
    super.initState();
    _summary = GetIt.instance<CheckoutGateway>().getSummary(widget.sessionId);
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<CheckoutSummary>(
      future: _summary,
      builder: (context, snap) {
        if (snap.hasError) {
          return Scaffold(
            body: Center(child: Text('${snap.error}')),
          );
        }
        if (!snap.hasData) {
          return const Scaffold(
              body: Center(child: CircularProgressIndicator()));
        }
        return BlocProvider(
          create: (_) => CheckoutPaymentBloc(
            gateway: GetIt.instance<CheckoutGateway>(),
            sessionId: widget.sessionId,
            initial: snap.data!,
          ),
          child: PaymentStepScreen(sessionId: widget.sessionId),
        );
      },
    );
  }
}

/// Review-step route helper. Same summary-prefetch + cached-future
/// pattern as the payment route — needed so the review bloc seeds its
/// initial state with the rendered totals + line items the user will
/// confirm.
class _CheckoutReviewRoute extends StatefulWidget {
  const _CheckoutReviewRoute({required this.sessionId});
  final String sessionId;

  @override
  State<_CheckoutReviewRoute> createState() => _CheckoutReviewRouteState();
}

class _CheckoutReviewRouteState extends State<_CheckoutReviewRoute> {
  late final Future<CheckoutSummary> _summary;

  @override
  void initState() {
    super.initState();
    _summary = GetIt.instance<CheckoutGateway>().getSummary(widget.sessionId);
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<CheckoutSummary>(
      future: _summary,
      builder: (context, snap) {
        if (snap.hasError) {
          return Scaffold(
            body: Center(child: Text('${snap.error}')),
          );
        }
        if (!snap.hasData) {
          return const Scaffold(
              body: Center(child: CircularProgressIndicator()));
        }
        return BlocProvider(
          create: (_) => CheckoutReviewBloc(
            gateway: GetIt.instance<CheckoutGateway>(),
            sessionId: widget.sessionId,
            initialSummary: snap.data!,
          ),
          child: ReviewScreen(sessionId: widget.sessionId),
        );
      },
    );
  }
}
