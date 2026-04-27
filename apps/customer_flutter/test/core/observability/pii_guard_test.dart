import 'dart:io';

import 'package:customer_flutter/core/observability/telemetry_adapter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('every allowed event has a property allow-list', () {
    for (final e in kAllowedTelemetryEvents) {
      expect(kAllowedTelemetryProps.containsKey(e), isTrue,
          reason: 'event "$e" lacks an entry in kAllowedTelemetryProps');
    }
  });

  test('client-events.md events match the in-code allow-list', () {
    final f = File('../../specs/phase-1C/014-customer-app-shell/contracts/client-events.md');
    if (!f.existsSync()) {
      // CI runs from the app dir; skip when contract file is unreachable.
      return;
    }
    final body = f.readAsStringSync();
    // Event names always contain a dot (e.g. `app.cold_start`); property
    // identifiers (`bg_duration_ms`) don't — that's enough to disambiguate
    // first-column entries from property-list cells.
    final tableRows = RegExp(r'\|\s*`([a-z][a-z_]+\.[a-z._]+)`\s*\|', multiLine: true)
        .allMatches(body)
        .map((m) => m.group(1)!)
        .toSet();
    // The in-code set must be a superset of the contract table; new events
    // land in the doc + here together.
    for (final row in tableRows) {
      expect(kAllowedTelemetryEvents.contains(row), isTrue,
          reason: 'contract event "$row" missing from allow-list');
    }
  });

  test('NoopTelemetryAdapter accepts any allowed event without error', () {
    const t = NoopTelemetryAdapter();
    for (final e in kAllowedTelemetryEvents) {
      t.emit(e, properties: const {});
    }
  });
}
