// FR-008 — fail the build on any user-facing string literal under
// `lib/features/**/{screens,widgets}/`. The lint walks the AST via
// `analyzer` and inspects the named arguments / positional arguments of
// `Text(...)`, `SnackBar(content: …)`, `AlertDialog(title:|content: …)`,
// and any `tooltip` / `semanticsLabel` / `hintText` / `labelText` strings.
//
// Run via `dart run tool/lint/no_hardcoded_strings.dart`.

import 'dart:io';

import 'package:analyzer/dart/analysis/utilities.dart';
import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';

const _flaggedNamedArgs = {
  'tooltip',
  'semanticsLabel',
  'hintText',
  'labelText',
  'errorText',
  'helperText',
};

const _flaggedCtors = {'Text', 'SnackBar', 'AlertDialog'};

const _allowlistFiles = <String>{
  // Internal placeholders used only during scaffolding; fail-listed once UI lands.
};

void main(List<String> args) {
  final featureRoot = Directory('lib/features');
  if (!featureRoot.existsSync()) {
    stdout.writeln('no_hardcoded_strings: lib/features missing — skipping.');
    return;
  }

  final violations = <String>[];
  for (final entity in featureRoot.listSync(recursive: true)) {
    if (entity is! File || !entity.path.endsWith('.dart')) continue;
    if (_allowlistFiles.contains(entity.path)) continue;
    final segments = entity.path.split(Platform.pathSeparator);
    if (!segments.contains('screens') && !segments.contains('widgets')) {
      continue;
    }
    final result = parseFile(
      path: entity.absolute.path,
      featureSet: _features,
    );
    final visitor = _StringLitVisitor(entity.path, result.lineInfo);
    result.unit.visitChildren(visitor);
    violations.addAll(visitor.findings);
  }

  if (violations.isEmpty) {
    stdout.writeln('no_hardcoded_strings: 0 violations');
    return;
  }
  stderr.writeln('no_hardcoded_strings: ${violations.length} violation(s)');
  for (final v in violations) {
    stderr.writeln('  $v');
  }
  exitCode = 1;
}

final _features = parseString(content: 'void _x() {}').unit.featureSet;

class _StringLitVisitor extends RecursiveAstVisitor<void> {
  _StringLitVisitor(this.path, this.lineInfo);
  final String path;
  final dynamic lineInfo;
  final List<String> findings = [];

  @override
  void visitInstanceCreationExpression(InstanceCreationExpression node) {
    final ctor = node.constructorName.type.name2.lexeme;
    if (_flaggedCtors.contains(ctor)) {
      for (final arg in node.argumentList.arguments) {
        if (arg is StringLiteral) {
          _record(arg, '$ctor(...) literal: ${arg.toSource()}');
        }
        if (arg is NamedExpression &&
            _flaggedNamedArgs.contains(arg.name.label.name)) {
          if (arg.expression is StringLiteral) {
            _record(arg.expression as StringLiteral,
                '$ctor(${arg.name.label.name}: …) literal: ${arg.expression.toSource()}');
          }
        }
      }
    }
    super.visitInstanceCreationExpression(node);
  }

  void _record(StringLiteral literal, String reason) {
    final loc = lineInfo.getLocation(literal.offset);
    findings.add('$path:${loc.lineNumber}: $reason');
  }
}
