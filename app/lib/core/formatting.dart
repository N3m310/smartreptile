/// Formatting rules shared by the app and the dashboard (`02-design/04` §7).
///
/// These are pure functions so they can be unit-tested without a widget tree.
library;

class MetricFormat {
  const MetricFormat._();

  /// Decimal places per metric, matching the server's metric dictionary precision.
  static int precisionFor(String metricCode) {
    switch (metricCode) {
      case 'tempC':
      case 'surfaceTempC':
        return 1; // 28.6 °C on a card; tables use the raw 2-decimal value
      case 'humidityPct':
        return 0;
      case 'lightLux':
        return 0;
      case 'uvIndex':
        return 1;
      default:
        return 1;
    }
  }

  /// Formats a value for display: 28.6, 41, 1 820, 0.3.
  static String value(double value, String metricCode) {
    final digits = precisionFor(metricCode);
    final text = value.toStringAsFixed(digits);
    return metricCode == 'lightLux' ? _groupThousands(text) : text;
  }

  static String _groupThousands(String text) {
    final parts = text.split('.');
    final buffer = StringBuffer();
    final digits = parts.first;

    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) {
        buffer.write(' ');
      }
      buffer.write(digits[i]);
    }

    return parts.length > 1
        ? '${buffer.toString()}.${parts[1]}'
        : buffer.toString();
  }

  /// Relative age in the form the app shows: "12 s", "12 min", "3 h".
  static String relativeAge(Duration age) {
    if (age.inSeconds < 60) {
      return '${age.inSeconds} s';
    }
    if (age.inMinutes < 60) {
      return '${age.inMinutes} min';
    }
    return '${age.inHours} h';
  }

  /// Plural bucket used to pick the localised "x seconds ago" message.
  static AgeBucket ageBucket(Duration age) {
    if (age.inSeconds < 60) {
      return AgeBucket.seconds;
    }
    if (age.inMinutes < 60) {
      return AgeBucket.minutes;
    }
    return AgeBucket.hours;
  }

  /// Count that goes with [ageBucket].
  static int ageCount(Duration age) {
    switch (ageBucket(age)) {
      case AgeBucket.seconds:
        return age.inSeconds;
      case AgeBucket.minutes:
        return age.inMinutes;
      case AgeBucket.hours:
        return age.inHours;
    }
  }
}

enum AgeBucket { seconds, minutes, hours }
