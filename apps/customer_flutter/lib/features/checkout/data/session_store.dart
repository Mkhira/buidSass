import 'package:shared_preferences/shared_preferences.dart';

/// Persists the active checkout session id so a cold-start mid-flow can
/// resume at `/checkout/{id}/summary` (T-4.17). Cleared on confirmation
/// + on cart clear-after-submit-success.
class CheckoutSessionStore {
  CheckoutSessionStore({required this.prefs});
  static const _key = 'checkout.session_id.v1';

  final SharedPreferences prefs;

  String? read() => prefs.getString(_key);

  Future<void> save(String sessionId) => prefs.setString(_key, sessionId);

  Future<void> clear() => prefs.remove(_key);
}
