/// Every user-facing string of the prototype, in one file.
///
/// **Why not the ARB files?** Because the prototype is English-only by decision (the user chose it), and the app's
/// translation catalogue belongs to the app that ships. Adding ~200 strings to `app_en.arb` that will never be
/// translated into Vietnamese would (a) make the real catalogue look complete when it is not, (b) make the prototype
/// look permanent, and (c) turn a deletable artefact into a merge conflict on the day the real screens arrive.
///
/// The convention the docs actually protect is *no user-facing literals scattered through widgets*. This file satisfies
/// it, keeps the strings greppable in one place, and makes promoting them to ARB — if anyone ever wants to — a
/// mechanical move rather than an archaeology exercise.
///
/// Naming: `screenElement` — e.g. [alertsResolveReasonHeading]. Anything that needs a value is a function.
library;

/// Labels for the prototype UI.
class Labels {
  const Labels._();

  // -----------------------------------------------------------------------------------------------------------
  // Shell, navigation and the prototype banner
  // -----------------------------------------------------------------------------------------------------------

  /// App bar title on the dashboard.
  static const appTitle = 'SmartReptile';

  /// Bottom navigation label for Home.
  static const tabHome = 'Home';

  /// Bottom navigation label for History.
  static const tabHistory = 'History';

  /// Bottom navigation label for Alerts.
  static const tabAlerts = 'Alerts';

  /// Bottom navigation label for the More menu.
  static const tabMore = 'More';

  /// More menu: terrariums.
  static const moreTerrariums = 'Terrariums';

  /// More menu: devices.
  static const moreDevices = 'Devices';

  /// More menu: daily report.
  static const moreReport = 'Report';

  /// More menu: settings.
  static const moreSettings = 'Settings';

  /// More menu: the rule lab.
  static const moreRuleLab = 'Rule Lab';

  /// More menu: diagnostics.
  static const moreDiagnostics = 'Diagnostics';

  /// Banner title: this is not the real app.
  static const prototypeTitle = 'Prototype';

  /// Banner body: fake data, real rules.
  static const prototypeBody =
      'Fake measurements and fake devices. Every threshold, dwell, hysteresis and suppression rule is the real one.';

  /// Banner action: open the rule lab.
  static const prototypeOpenLab = 'Open the Rule Lab';

  /// The demo-clock control label.
  static const demoClockLabel = 'Demo clock';

  /// Advances the demo clock by ten minutes.
  static const demoClockAdvance = '+10 min';

  /// Resets the demo clock to the instant the fake data ends.
  static const demoClockReset = 'Reset';

  /// Explains the demo clock.
  static const demoClockHint =
      'Frozen at the instant the fake data ends, so a screenshot can be re-checked tomorrow.';

  // -----------------------------------------------------------------------------------------------------------
  // Sign in and sign up
  // -----------------------------------------------------------------------------------------------------------

  /// Login screen title.
  static const loginTitle = 'Sign in';

  /// Login screen subtitle.
  static const loginSubtitle = 'Your terrariums, wherever you are.';

  /// Email field label.
  static const fieldEmail = 'Email';

  /// Password field label.
  static const fieldPassword = 'Password';

  /// Name field label.
  static const fieldName = 'Name';

  /// Stay-signed-in switch.
  static const loginStaySignedIn = 'Stay signed in';

  /// Login button.
  static const loginAction = 'Sign in';

  /// Link to the registration screen.
  static const loginToRegister = 'No account yet? Create one';

  /// Register screen title.
  static const registerTitle = 'Create an account';

  /// Register button.
  static const registerAction = 'Create account';

  /// Link back to the login screen.
  static const registerToLogin = 'Already have an account? Sign in';

  /// Note explaining the prototype's login.
  static const loginDemoNote =
      'Prototype: the credentials are pre-filled. Nothing is stored, and no real account exists.';

  /// Sign out.
  static const signOut = 'Sign out';

  // -----------------------------------------------------------------------------------------------------------
  // Dashboard (S4)
  // -----------------------------------------------------------------------------------------------------------

  /// Header for a terrarium with no data.
  static const dashboardEmptyTitle = 'No reading yet';

  /// Empty state when nothing is claimed.
  static const dashboardEmptyBody =
      'Claim a sensor node and readings will appear here within a minute.';

  /// Empty state when a node is bound but silent.
  static const dashboardWaitingTitle = 'Waiting for the first reading';

  /// Explanation for the waiting state.
  static const dashboardWaitingBody =
      'The node is claimed and online. It has not sent a sample yet.';

  /// Banner shown while a silence window is active.
  static const dashboardSilenceBanner = 'Silenced — alerts are still recorded';

  /// Banner shown when the device is offline.
  static const dashboardOfflineBanner =
      'No data for a while. Evaluation is paused, which is not the same as safe.';

  /// Banner shown when the device is in maintenance mode.
  static const dashboardMaintenanceBanner =
      'Maintenance mode: alerts are recorded but not sent.';

  /// One open alert, with its summary.
  static String dashboardOpenAlert(String summary) => '1 open alert: $summary';

  /// Several open alerts.
  static String dashboardOpenAlerts(int count) => '$count open alerts';

  /// Nothing is wrong.
  static const dashboardAllClear = 'Nothing out of range';

  /// Today's strip heading.
  static const dashboardToday = 'Today';

  /// Today's strip: out-of-range minutes.
  static String dashboardOutOfRange(int minutes) => '$minutes min out of range';

  /// Today's strip: light hours.
  static String dashboardLightHours(String hours, int required) =>
      '$hours light h of $required required';

  /// Today's strip: coverage.
  static String dashboardCoverage(String percent) => 'coverage $percent%';

  /// Today's strip: exposure.
  static String dashboardExposure(String degreeHours) =>
      '$degreeHours °C·h exposure';

  /// Links a card to the Rule Lab explanation.
  static const dashboardWhyThisNumber = 'Why this number?';

  /// Label for the last update time.
  static String dashboardLastUpdated(String age) => 'last updated $age ago';

  /// Label for a value that has never arrived.
  static const dashboardNeverReceived = 'never received';

  // -----------------------------------------------------------------------------------------------------------
  // History (S5)
  // -----------------------------------------------------------------------------------------------------------

  /// Range chip: one hour.
  static const historyRange1h = '1 h';

  /// Range chip: six hours.
  static const historyRange6h = '6 h';

  /// Range chip: twenty-four hours.
  static const historyRange24h = '24 h';

  /// Range chip: forty-eight hours.
  static const historyRange48h = '48 h';

  /// Range chip: seven days, which needs rollups.
  static const historyRange7d = '7 d';

  /// Range chip: thirty days, which needs rollups.
  static const historyRange30d = '30 d';

  /// Why the longer ranges are unavailable in the prototype.
  static const historyRollupNote =
      'Raw readings cover 6 hours, 5-minute averages 48 hours. Beyond that the server serves hourly rollups — '
      'an M4 pipeline this prototype does not pretend to have, so it offers 1 h to 48 h only.';

  /// Note shown when a range has no samples.
  static const historyNoData = 'No samples in this range.';

  /// Chart legend: target band.
  static const historyTargetBand = 'target band';

  /// Chart legend: alert.
  static const historyAlertBand = 'alert';

  /// Chart statistics row.
  static String historyStats(String min, String avg, String max) =>
      'min $min · avg $avg · max $max';

  // -----------------------------------------------------------------------------------------------------------
  // Alerts (S6, S7)
  // -----------------------------------------------------------------------------------------------------------

  /// Title of the alert inbox.
  static const alertsTitle = 'Alerts';

  /// Empty inbox.
  static const alertsEmpty = 'No alerts match this filter.';

  /// Filter chip: everything.
  static const alertsFilterAll = 'All';

  /// Filter label: severity.
  static const alertsFilterSeverity = 'Severity';

  /// Filter label: state.
  static const alertsFilterState = 'State';

  /// Clears the filter.
  static const alertsClearFilter = 'Clear';

  /// Severity: warning.
  static const severityWarning = 'Warning';

  /// Severity: critical.
  static const severityCritical = 'Critical';

  /// Severity: info.
  static const severityInfo = 'Info';

  /// State: open.
  static const stateOpen = 'Open';

  /// State: acknowledged.
  static const stateAcknowledged = 'Acknowledged';

  /// State: resolved.
  static const stateResolved = 'Resolved';

  /// Alert detail heading for when it started.
  static const alertTriggered = 'Triggered';

  /// Alert detail heading for when it ended.
  static const alertResolvedAt = 'Recovered';

  /// Alert detail heading for how long it lasted.
  static const alertDuration = 'Duration';

  /// Alert detail heading for the worst value seen.
  static const alertPeak = 'Peak';

  /// Alert detail heading for the band in force.
  static const alertBandInForce = 'Band in force';

  /// Alert detail heading for where the band came from.
  static const alertThresholdSource = 'Threshold source';

  /// Alert detail heading for the value timeline.
  static const alertTimeline = 'Value timeline';

  /// Description of the band drawn on the timeline.
  static const alertTimelineBandNote =
      'The shaded band is the target range that was in force. The line is the measured value.';

  /// The action to acknowledge an alert.
  static const alertAcknowledge = 'Acknowledge';

  /// Heading above the resolve reasons.
  static const alertResolveHeading = 'Resolve as';

  /// Resolve reason: recovered.
  static const resolveRecovered = 'Recovered';

  /// Resolve reason: false positive.
  static const resolveFalsePositive = 'False positive';

  /// Resolve reason: sensor fault.
  static const resolveSensorFault = 'Sensor fault';

  /// Resolve reason: accepted risk.
  static const resolveAccepted = 'Accepted risk';

  /// The resolve button.
  static const alertResolve = 'Resolve';

  /// Note field label.
  static const alertNote = 'Note (optional)';

  /// Shown for an alert that is already closed.
  static const alertTerminalNote =
      'Resolved is final. A new excursion creates a new alert row — history is never rewritten.';

  /// Heading for the notification log of one alert.
  static const alertNotifications = 'Notifications for this alert';

  /// Why a notification was suppressed: silence.
  static const suppressionSilenced = 'Silenced';

  /// Why a notification was suppressed: quiet hours.
  static const suppressionQuietHours = 'Quiet hours';

  /// Why a notification was suppressed: maintenance.
  static const suppressionMaintenance = 'Maintenance mode';

  /// Why a notification was suppressed: below the minimum severity.
  static const suppressionBelowMinSeverity = 'Below your minimum severity';

  /// Why a notification was suppressed: hourly cap.
  static const suppressionDigest = 'Coalesced into the hourly digest';

  /// Why a notification was suppressed: preference.
  static const suppressionPreference = 'Switched off in your settings';

  /// Why a notification was suppressed: no channel.
  static const suppressionNoChannel = 'No channel enabled';

  /// A delivered notification.
  static const notificationSent = 'Sent';

  /// Heading for the suppression summary.
  static const alertSuppressionSummary = 'Why nothing was sent';

  /// Label explaining the acknowledgement record.
  static String alertAcknowledgedBy(String name) => 'Acknowledged by $name';

  /// The dedupe explanation shown on the detail screen.
  static const alertDedupeNote =
      'One incident is one row. Repeated excursions extend this alert instead of creating new ones, '
      'and the start time is back-dated to the first reading that left the band.';

  // -----------------------------------------------------------------------------------------------------------
  // Thresholds (S9)
  // -----------------------------------------------------------------------------------------------------------

  /// Title.
  static const thresholdsTitle = 'Thresholds';

  /// Row label for the target band.
  static const thresholdsTarget = 'Target';

  /// Row label for the critical band.
  static const thresholdsCritical = 'Critical';

  /// Row label for the dwell timers.
  static const thresholdsDwell = 'Dwell warn / crit';

  /// Row label for the recovery margin.
  static const thresholdsRecovery = 'Recovery margin';

  /// Row label for the provenance.
  static const thresholdsSource = 'Source';

  /// Row label for the phase.
  static const thresholdsPhase = 'Phase';

  /// The band in force right now.
  static const thresholdsInForce = 'in force now';

  /// The band that applies at another time of day.
  static const thresholdsNotInForce = 'not in force now';

  /// Marks an inert accumulated band.
  static const thresholdsAccumulated = 'accumulated rule, not per sample';

  /// Action: add an override.
  static const thresholdsAddOverride = 'Override for this terrarium';

  /// Action: remove an override.
  static const thresholdsClearOverride = 'Remove override';

  /// Dialog title for editing a band.
  static const thresholdsEditTitle = 'Edit band';

  /// Save action.
  static const thresholdsSave = 'Save override';

  /// The profile caveat heading.
  static const thresholdsProfileNote = 'Profile note';

  /// The citation heading.
  static const thresholdsCitation = 'Provenance';

  /// The provenance caveat that must never be dropped.
  static const thresholdsCitationCaveat =
      'Typical published husbandry range. The dwell and recovery values are team design choices, not literature.';

  /// Heading above the validation findings.
  static const thresholdsValidation = 'Validation';

  /// A blocking finding.
  static const thresholdsBlocking = 'This band cannot be saved';

  /// A non-blocking finding.
  static const thresholdsWarningOnly = 'Saved anyway — just telling you';

  /// Unit for minutes.
  static const unitMinutes = 'min';

  /// Field label for the target minimum.
  static const fieldTargetMin = 'Target min';

  /// Field label for the target maximum.
  static const fieldTargetMax = 'Target max';

  /// Field label for the critical minimum.
  static const fieldCriticalMin = 'Critical min';

  /// Field label for the critical maximum.
  static const fieldCriticalMax = 'Critical max';

  /// Field label for the warning dwell.
  static const fieldDwellWarn = 'Warning dwell (min)';

  /// Field label for the critical dwell.
  static const fieldDwellCrit = 'Critical dwell (min)';

  /// Field label for the recovery margin.
  static const fieldRecoveryMargin = 'Recovery margin';

  /// Validation message for a non-numeric field.
  static const invalidNumber = 'Enter a number.';

  // -----------------------------------------------------------------------------------------------------------
  // Devices (S10) and claiming (S11)
  // -----------------------------------------------------------------------------------------------------------

  /// Title of the fleet view.
  static const devicesTitle = 'Devices';

  /// Section heading for claimed nodes.
  static const devicesBound = 'Bound';

  /// Section heading for unclaimed nodes.
  static const devicesUnbound = 'Waiting to be claimed';

  /// Device status: online.
  static const deviceOnline = 'online';

  /// Device status: offline.
  static const deviceOffline = 'offline';

  /// Device status: maintenance.
  static const deviceMaintenance = 'maintenance';

  /// Device status: provisioning.
  static const deviceProvisioning = 'provisioning';

  /// Device status: revoked.
  static const deviceRevoked = 'revoked';

  /// Row label: firmware.
  static const devicesFirmware = 'Firmware';

  /// Row label: last seen.
  static const devicesLastSeen = 'Last seen';

  /// Row label: signal.
  static const devicesSignal = 'Signal';

  /// Row label: battery.
  static const devicesBattery = 'Battery';

  /// Row label: uptime.
  static const devicesUptime = 'Uptime';

  /// Row label: free heap.
  static const devicesHeap = 'Free heap';

  /// Row label: bound terrarium.
  static const devicesBoundTo = 'Bound to';

  /// Action: enter the claim flow.
  static const devicesClaim = 'Add a device';

  /// Action: rebind a device.
  static const devicesRebind = 'Rebind';

  /// Action: revoke a device.
  static const devicesRevoke = 'Revoke';

  /// Confirmation for revoking.
  static const devicesRevokeConfirm =
      'Revoke this node? It will no longer be able to authenticate. The readings it already sent are kept.';

  /// Action: rotate credentials.
  static const devicesRotate = 'Rotate secret';

  /// Heading for the calibration section.
  static const devicesCalibration = 'Calibration offsets';

  /// Explanation of calibration.
  static const devicesCalibrationNote =
      'Applied when a reading arrives. The raw value is kept for review.';

  /// Field label: temperature offset.
  static const fieldTempOffset = 'Temperature offset (°C)';

  /// Field label: humidity offset.
  static const fieldRhOffset = 'Humidity offset (%RH)';

  /// Field label: lux gain.
  static const fieldLuxGain = 'Lux gain (×)';

  /// Switch label for maintenance mode.
  static const devicesMaintenanceSwitch = 'Maintenance mode';

  /// Explanation of maintenance mode.
  static const devicesMaintenanceNote =
      'Records every alert and sends none. For cleaning and lamp changes.';

  /// Title of the claim screen.
  static const claimTitle = 'Add a device';

  /// Claim step one.
  static const claimStepOne =
      'Power the node. Its screen shows an 8-character code.';

  /// Claim step two.
  static const claimStepTwo = 'Type the code below and pick a terrarium.';

  /// Label of the code field.
  static const claimCodeLabel = 'Pairing code';

  /// Hint inside the code field.
  static const claimCodeHint = 'K7M2-QP4T';

  /// The code expiring soon.
  static String claimCodeExpires(String remaining) =>
      'The code expires in $remaining';

  /// Label of the terrarium picker.
  static const claimTerrariumLabel = 'Terrarium';

  /// The bind button.
  static const claimAction = 'Bind device';

  /// Explanation of rotating codes.
  static const claimHelp =
      'The node shows a new code every 15 minutes, and an old one stops working.';

  /// Confirmation after a successful claim.
  static const claimSuccess =
      'Node claimed. Readings should appear within a minute.';

  // -----------------------------------------------------------------------------------------------------------
  // Report (S12) and history-independent summaries
  // -----------------------------------------------------------------------------------------------------------

  /// Title.
  static const reportTitle = 'Daily report';

  /// Column heading: day.
  static const reportDay = 'Day';

  /// Column heading: coverage.
  static const reportCoverage = 'Coverage';

  /// Column heading: out-of-range minutes.
  static const reportOutOfRange = 'Out of range';

  /// Column heading: exposure.
  static const reportExposure = 'Exposure';

  /// Column heading: light hours.
  static const reportLight = 'Light';

  /// Column heading: alerts.
  static const reportAlerts = 'Alerts';

  /// Column heading: silence minutes.
  static const reportSilence = 'Silenced';

  /// Badge for a low-confidence row.
  static const reportLowConfidence = 'low confidence';

  /// The rule that makes coverage mandatory next to compliance.
  static const reportCoverageRule =
      'Coverage is shown next to every summary: a 100 %-compliant day with 40 % of its data is not compliance.';

  /// Explanation of the exposure index.
  static const reportExposureNote =
      'Exposure is in degree-hours: 60 minutes at 2 °C over target scores twice as high as 60 minutes at 1 °C.';

  /// Explanation of the light deficit.
  static const reportLightNote =
      'Light is an accumulated rule, not a per-sample band: a passing cloud is not an incident, a failed lamp is.';

  /// Export action.
  static const reportExport = 'Export CSV';

  /// Why export is not available.
  static const reportExportUnavailable =
      'Export is generated asynchronously by the server and downloaded from a link. Not in this prototype.';

  // -----------------------------------------------------------------------------------------------------------
  // Settings (S14)
  // -----------------------------------------------------------------------------------------------------------

  /// Title.
  static const settingsTitle = 'Settings';

  /// Section heading: account.
  static const settingsAccount = 'Account';

  /// Row label: role.
  static const settingsRole = 'Role';

  /// Row label: email.
  static const settingsEmail = 'Email';

  /// Section heading: appearance.
  static const settingsAppearance = 'Appearance';

  /// Row label: theme.
  static const settingsTheme = 'Theme';

  /// Theme option: system.
  static const themeSystem = 'System';

  /// Theme option: light.
  static const themeLight = 'Light';

  /// Theme option: dark.
  static const themeDark = 'Dark';

  /// Row label: language.
  static const settingsLanguage = 'Language';

  /// Why there is only one language.
  static const settingsLanguageNote = 'Prototype: English only.';

  /// Section heading: notifications.
  static const settingsNotifications = 'Notifications';

  /// Row label: in-app inbox.
  static const settingsChannelInApp = 'In-app inbox';

  /// Row label: push.
  static const settingsChannelPush = 'Push (Android)';

  /// Row label: Telegram.
  static const settingsChannelTelegram = 'Telegram';

  /// Row label: email.
  static const settingsChannelEmail = 'Email';

  /// Row label: minimum severity to notify.
  static const settingsMinSeverity = 'Notify me from';

  /// Row label: quiet hours.
  static const settingsQuietHours = 'Quiet hours';

  /// Quiet hours window text.
  static String settingsQuietHoursWindow(int start, int end) =>
      '${start.toString().padLeft(2, '0')}:00 – ${end.toString().padLeft(2, '0')}:00';

  /// Explanation that a Critical bypasses quiet hours.
  static const settingsQuietHoursNote =
      'A Critical always gets through. A Warning waits until the morning.';

  /// Row label: whether recovery notices are sent.
  static const settingsRecoveryNotices = 'Tell me when it recovers';

  /// Section heading: silences.
  static const settingsSilences = 'Active silences';

  /// Action: create a silence.
  static const settingsAddSilence = 'Silence a metric';

  /// Field label: silence reason.
  static const fieldSilenceReason = 'Reason (required)';

  /// Field label: silence duration.
  static const fieldSilenceDuration = 'Duration (hours)';

  /// Field label: silence metric.
  static const fieldSilenceMetric = 'Metric';

  /// Ends a silence.
  static const settingsEndSilence = 'End now';

  /// Section heading: prototype.
  static const settingsPrototype = 'Prototype';

  /// Section heading: about.
  static const settingsAbout = 'About';

  /// Explains what the prototype is.
  static const settingsAboutBody =
      'A clickable prototype of the main flow with fake data, built to examine the business rules before the '
      'backend endpoints exist. Screens, charts and the alert history are generated by the same rule kernel that '
      'the Rule Lab plays back.';

  // -----------------------------------------------------------------------------------------------------------
  // Rule Lab (the reason the prototype exists)
  // -----------------------------------------------------------------------------------------------------------

  /// Title.
  static const labTitle = 'Rule Lab';

  /// Intro line.
  static const labIntro =
      'Each scenario is a claim taken from the documentation and turned into data. Press play and read the decision '
      'trace: every step names the rule that produced it.';

  /// Heading above the scenario list.
  static const labScenarios = 'Scenarios';

  /// Heading: the question the scenario answers.
  static const labQuestion = 'Question';

  /// Heading: why it matters.
  static const labWhyItMatters = 'Why it matters';

  /// Heading: where the claim comes from.
  static const labDocRef = 'Documentation';

  /// Heading: what the documentation says must happen.
  static const labExpectation = 'What the documentation says';

  /// Badge: the engine agrees with the documentation.
  static const labMatchesDoc = 'Matches the documentation';

  /// Badge: the engine agrees with the rule, but the document's own worked example does not.
  static const labDocExampleDiffers = 'Rule verified — worked example differs';

  /// Badge: the engine disagrees with the documentation.
  static const labDisagreesWithDoc = 'Disagrees with the documentation';

  /// Heading above the disagreement explanation.
  static const labDocDisagreement =
      'Where the documents disagree with each other';

  /// Heading above the parameter toggles.
  static const labToggles = 'Engine parameters';

  /// Explains the toggles.
  static const labTogglesNote =
      'Dwell and the recovery margin are team design choices, not literature values. Change them and watch the same '
      'data produce different decisions — that is what makes them reviewable.';

  /// Field label: warning dwell.
  static const labDwellWarn = 'Warning dwell';

  /// Field label: critical dwell.
  static const labDwellCrit = 'Critical dwell';

  /// Field label: recovery minutes.
  static const labRecoveryMinutes = 'Recovery minutes';

  /// Field label: recovery mode.
  static const labRecoveryMode = 'Recovery margin applies';

  /// Resets the toggles.
  static const labResetToggles = 'Reset to documented defaults';

  /// Play control.
  static const labPlay = 'Play';

  /// Pause control.
  static const labPause = 'Pause';

  /// Step-back control.
  static const labStepBack = 'Back';

  /// Step-forward control.
  static const labStep = 'Step';

  /// Restart control.
  static const labRestart = 'Restart';

  /// Timeline position, e.g. "minute 14 of 58".
  static String labMinute(int minute, int total) => 'minute $minute of $total';

  /// Heading above the decision trace.
  static const labTrace = 'Decision trace';

  /// Table heading: rule.
  static const labTraceRule = 'Rule';

  /// Table heading: time.
  static const labTraceTime = 'Time';

  /// Table heading: decision.
  static const labTraceDecision = 'Decision';

  /// Table heading: explanation.
  static const labTraceExplanation = 'Why';

  /// Trace filter: only decisions that changed something.
  static const labTraceNoteworthy = 'Only decisions that changed something';

  /// Trace filter: every evaluated sample.
  static const labTraceEverything = 'Every sample, including the quiet ones';

  /// Heading above the alert episodes the run produced.
  static const labAlertsProduced = 'Alerts this run produced';

  /// No alerts were produced.
  static const labNoAlerts =
      'No alert. That is the result, not an error — dwell filtered the spike out.';

  /// Heading above the run counters.
  static const labCounters = 'Run counters';

  /// Counter: readings evaluated.
  static const labCounterEvaluated = 'readings evaluated';

  /// Counter: readings skipped.
  static const labCounterSkipped = 'readings skipped (flagged or unmeasured)';

  /// Counter: alerts opened.
  static const labCounterOpened = 'alerts opened';

  /// Counter: escalations.
  static const labCounterEscalated = 'escalations';

  /// Counter: resolutions.
  static const labCounterResolved = 'resolutions';

  /// Counter: notifications sent.
  static const labCounterSent = 'notifications sent';

  /// Counter: notifications suppressed.
  static const labCounterSuppressed = 'notifications suppressed';

  /// Counter: back-filled readings.
  static const labCounterBackfilled = 'readings that arrived late';

  /// Heading above the rule coverage matrix.
  static const labCoverage = 'Rule coverage';

  /// Explains the coverage matrix.
  static const labCoverageNote =
      'Which rules this prototype actually exercises, and which are specified but not modelled here. The empty rows '
      'are the honest part: they are a to-do list, not a claim.';

  /// Coverage status: modelled.
  static const labCoverageModelled = 'modelled';

  /// Coverage status: not modelled.
  static const labCoverageNotModelled = 'not modelled';

  /// Coverage status: partially modelled.
  static const labCoveragePartial = 'partly modelled';

  /// Heading above the open questions.
  static const labOpenQuestions = 'Open questions this prototype found';

  /// Explains the open-questions panel.
  static const labOpenQuestionsNote =
      'Each of these is a place where the documents do not agree with themselves or do not say. They need a decision, '
      'not code.';

  /// Heading above the rule text of the selected trace step.
  static const labStepRuleText = 'Rule as written';

  /// Shown when a scenario produces an alert but the trace filter hides it.
  static const labTraceHiddenByFilter =
      'Some steps are hidden by the filter above.';

  // -----------------------------------------------------------------------------------------------------------
  // Diagnostics (S16)
  // -----------------------------------------------------------------------------------------------------------

  /// Title.
  static const diagnosticsTitle = 'Diagnostics';

  /// Row label: coverage.
  static const diagnosticsCoverage = 'Coverage';

  /// Row label: last sample.
  static const diagnosticsLastSample = 'Last sample';

  /// Row label: expected interval.
  static const diagnosticsInterval = 'Expected interval';

  /// Row label: samples received.
  static const diagnosticsReceived = 'Samples received';

  /// Row label: window.
  static const diagnosticsWindow = 'Observed window';

  /// Note that these numbers are honest.
  static const diagnosticsHonestNote =
      'Read-only, and deliberately boring: the two numbers that matter are coverage and the age of the last sample.';

  // -----------------------------------------------------------------------------------------------------------
  // Shared words
  // -----------------------------------------------------------------------------------------------------------

  /// Cancel.
  static const cancel = 'Cancel';

  /// Close.
  static const close = 'Close';

  /// Save.
  static const save = 'Save';

  /// Apply.
  static const apply = 'Apply';

  /// Retry.
  static const retry = 'Retry';

  /// None.
  static const none = 'None';

  /// Not available.
  static const notAvailable = '—';

  /// A metric code turned into a readable name.
  static const metricTempC = 'Temperature';

  /// Humidity.
  static const metricHumidityPct = 'Humidity';

  /// Light.
  static const metricLightLux = 'Light';

  /// UV index.
  static const metricUvIndex = 'UV index';

  /// Surface temperature.
  static const metricSurfaceTempC = 'Surface temperature';

  /// Device-silence alert name.
  static const metricDeviceSilent = 'Device silent';

  /// Gradient alert name.
  static const metricGradient = 'Surface gradient';

  /// Light-deficit alert name.
  static const metricLightDeficit = 'Light deficit';

  /// Clock skew alert name.
  static const metricClockSkew = 'Clock skew';

  /// Sensor fault alert name.
  static const metricSensorFault = 'Sensor fault';

  /// Phase: day.
  static const phaseDay = 'Day';

  /// Phase: night.
  static const phaseNight = 'Night';

  /// Phase: any.
  static const phaseAny = 'Any';

  /// Band source: override.
  static const sourceOverride = 'override';

  /// Band source: profile.
  static const sourceProfile = 'profile';

  /// Band source: system default.
  static const sourceDefault = 'default';

  /// Terrarium picker label.
  static const terrarium = 'Terrarium';

  /// Metric label.
  static const metric = 'Metric';

  /// Alert label.
  static const alert = 'Alert';

  /// Reading that was flagged as impossible.
  static const flaggedReading = 'Flagged reading';

  /// Convert a metric code into a readable name.
  static String metricName(String code) {
    switch (code) {
      case 'tempC':
        return metricTempC;
      case 'humidityPct':
        return metricHumidityPct;
      case 'lightLux':
        return metricLightLux;
      case 'uvIndex':
        return metricUvIndex;
      case 'surfaceTempC':
        return metricSurfaceTempC;
      default:
        return code;
    }
  }
}
