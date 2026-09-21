using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeciesProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ScientificName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ClimateZone = table.Column<int>(type: "int", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    PhotoperiodHours = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    LightsOnLocalTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    LightThresholdLux = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MinLightHoursPerDay = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeciesProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<byte[]>(type: "varbinary(64)", maxLength: 64, nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: false),
                    PasswordIterations = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuietHoursStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    QuietHoursEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    MinNotifySeverity = table.Column<int>(type: "int", nullable: false),
                    ChannelFcmEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ChannelTelegramEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ChannelEmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TelegramChatId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FcmToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Threshold",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeciesProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    TargetMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    TargetMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CriticalMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    CriticalMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    DwellWarnMinutes = table.Column<int>(type: "int", nullable: false),
                    DwellCritMinutes = table.Column<int>(type: "int", nullable: false),
                    RecoveryMargin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SourceRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Threshold", x => x.Id);
                    table.CheckConstraint("CK_Threshold_CriticalOrder", "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
                    table.CheckConstraint("CK_Threshold_DwellOrder", "[DwellCritMinutes] <= [DwellWarnMinutes] AND [DwellWarnMinutes] > 0");
                    table.CheckConstraint("CK_Threshold_TargetOrder", "[TargetMin] < [TargetMax]");
                    table.ForeignKey(
                        name: "FK_Threshold_SpeciesProfile_SpeciesProfileId",
                        column: x => x.SpeciesProfileId,
                        principalTable: "SpeciesProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshToken", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshToken_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Terrarium",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SpeciesProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terrarium", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terrarium_SpeciesProfile_SpeciesProfileId",
                        column: x => x.SpeciesProfileId,
                        principalTable: "SpeciesProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Terrarium_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ChipId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MacAddress = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Protocol = table.Column<int>(type: "int", nullable: false),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    ClaimCodeExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProvisionedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SamplingIntervalSec = table.Column<int>(type: "int", nullable: false),
                    PublishIntervalSec = table.Column<int>(type: "int", nullable: false),
                    CalibrationJson = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SignalStrengthDbm = table.Column<int>(type: "int", nullable: true),
                    BatteryPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    FreeHeapKb = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Device", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Device_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Device_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ThresholdOverride",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    TargetMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    TargetMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CriticalMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    CriticalMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    DwellWarnMinutes = table.Column<int>(type: "int", nullable: false),
                    DwellCritMinutes = table.Column<int>(type: "int", nullable: false),
                    RecoveryMargin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThresholdOverride", x => x.Id);
                    table.CheckConstraint("CK_ThresholdOverride_CriticalOrder", "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
                    table.CheckConstraint("CK_ThresholdOverride_TargetOrder", "[TargetMin] < [TargetMax]");
                    table.ForeignKey(
                        name: "FK_ThresholdOverride_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Alert",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    DedupeKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    TriggeringValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    PeakValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    BandMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    BandMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedReason = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alert", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alert_Device_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alert_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCredential",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    Salt = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GraceUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCredential", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCredential_Device_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceHealthSample",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RssiDbm = table.Column<int>(type: "int", nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    FreeHeapKb = table.Column<int>(type: "int", nullable: true),
                    BatteryPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    QualityFlags = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceHealthSample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceHealthSample_Device_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySample",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    QualityFlags = table.Column<int>(type: "int", nullable: false),
                    ClockSkewSeconds = table.Column<int>(type: "int", nullable: true),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetrySample_Device_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TelemetrySample_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetricReading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SampleId = table.Column<long>(type: "bigint", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    RawValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricReading", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetricReading_TelemetrySample_SampleId",
                        column: x => x.SampleId,
                        principalTable: "TelemetrySample",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alert_DedupeKey",
                table: "Alert",
                column: "DedupeKey",
                unique: true,
                filter: "[State] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_Alert_DeviceId",
                table: "Alert",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Alert_TerrariumId_State_TriggeredAt",
                table: "Alert",
                columns: new[] { "TerrariumId", "State", "TriggeredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Alert_TriggeredAt",
                table: "Alert",
                column: "TriggeredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Device_ChipId",
                table: "Device",
                column: "ChipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Device_LastSeenAt",
                table: "Device",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Device_PublicId",
                table: "Device",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Device_TerrariumId",
                table: "Device",
                column: "TerrariumId",
                unique: true,
                filter: "[TerrariumId] IS NOT NULL AND [Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_Device_UserId",
                table: "Device",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredential_DeviceId_RevokedAt",
                table: "DeviceCredential",
                columns: new[] { "DeviceId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceHealthSample_DeviceId_RecordedAt",
                table: "DeviceHealthSample",
                columns: new[] { "DeviceId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricReading_Metric_SampleId",
                table: "MetricReading",
                columns: new[] { "Metric", "SampleId" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricReading_SampleId_Metric",
                table: "MetricReading",
                columns: new[] { "SampleId", "Metric" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_UserId_FamilyId",
                table: "RefreshToken",
                columns: new[] { "UserId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesProfile_CreatedByUserId_Name",
                table: "SpeciesProfile",
                columns: new[] { "CreatedByUserId", "Name" },
                unique: true,
                filter: "[CreatedByUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesProfile_Name",
                table: "SpeciesProfile",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySample_DeviceId_Sequence",
                table: "TelemetrySample",
                columns: new[] { "DeviceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySample_RecordedAt",
                table: "TelemetrySample",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySample_TerrariumId_RecordedAt",
                table: "TelemetrySample",
                columns: new[] { "TerrariumId", "RecordedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Terrarium_SpeciesProfileId",
                table: "Terrarium",
                column: "SpeciesProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Terrarium_UserId",
                table: "Terrarium",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Threshold_SpeciesProfileId_Metric_Phase",
                table: "Threshold",
                columns: new[] { "SpeciesProfileId", "Metric", "Phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThresholdOverride_TerrariumId_Metric_Phase",
                table: "ThresholdOverride",
                columns: new[] { "TerrariumId", "Metric", "Phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Username",
                table: "User",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alert");

            migrationBuilder.DropTable(
                name: "DeviceCredential");

            migrationBuilder.DropTable(
                name: "DeviceHealthSample");

            migrationBuilder.DropTable(
                name: "MetricReading");

            migrationBuilder.DropTable(
                name: "RefreshToken");

            migrationBuilder.DropTable(
                name: "Threshold");

            migrationBuilder.DropTable(
                name: "ThresholdOverride");

            migrationBuilder.DropTable(
                name: "TelemetrySample");

            migrationBuilder.DropTable(
                name: "Device");

            migrationBuilder.DropTable(
                name: "Terrarium");

            migrationBuilder.DropTable(
                name: "SpeciesProfile");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
