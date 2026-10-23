// Transport MQTT: 0 = plaintext (cổng 1883), 1 = TLS (cổng 8883).
// Phải khai báo TRƯỚC các #include để chọn đúng thư viện mạng.
#define SR_MQTT_TLS 0

#include <WiFi.h>
#include <HTTPClient.h>
#include <Wire.h>
#include <BH1750.h>
#include <DHT.h>
#include <Preferences.h>
#include <PubSubClient.h>
#include "time.h"

#if SR_MQTT_TLS
  #include <WiFiClientSecure.h>
#endif

// =========================================================================
// SMARTREPTILE TELEMETRY NODE (ESP32)
//
// Transport chính : MQTT   sr/v1/d/<deviceId>/telemetry
// Transport dự phòng: HTTPS POST /api/v1/ingest/http
// Contract: docs/07-appendices/03-mqtt-and-rest-api-spec.md §3.2 / §3.6
//
// ĐIỀU KIỆN ĐỂ SERVER NHẬN DỮ LIỆU (Application/Ingest/DeviceAuthenticator.cs):
//   1. Device phải tồn tại trong DB với đúng deviceId.
//   2. Device phải đang được bind vào một terrarium.
//   3. Secret phải khớp credential đang hiệu lực (MQTT: username = deviceId, password = secret).
// => deviceId + secret chỉ trả về MỘT LẦN ở POST /api/v1/devices/claim. Repo không có secret mặc định.
//
// Nạp credential không cần build lại (lưu vào NVS, gõ trong Serial Monitor):
//   setwifi <ssid> <password>
//   set <deviceId> <secret>
//   show
//
// Không có secret nào trong source: repo là công khai, nên Wi-Fi lẫn device secret chỉ nằm ở NVS
// của board. Xem firmware/arduino/README.md.
// =========================================================================

// -------------------------------------------------------------------------
// 1. Wi-Fi
// -------------------------------------------------------------------------
// Credential Wi-Fi nạp lúc chạy bằng Serial `setwifi <ssid> <password>` rồi lưu vào NVS.
// Không hard-code: một mật khẩu Wi-Fi trong git là mật khẩu đã lộ, và nó phải nạp lại trên mọi board.
String ssid;
String password;

// -------------------------------------------------------------------------
// 2. Server
// -------------------------------------------------------------------------
// IP của máy tính đang chạy backend TRONG CÙNG mạng Wi-Fi với ESP32.
// Kiểm tra lại bằng: ipconfig   (dòng IPv4 Address của card Wi-Fi)
// Nếu IP đổi (DHCP cấp lại), chỉ cần sửa đúng dòng dưới rồi nạp lại.
#define SR_SERVER_IP "192.168.2.247"

// Port REST mặc định của dự án là 8080 (launchSettings.json / docker-compose.yml).
const char* apiUrl = "http://" SR_SERVER_IP ":8080/api/v1/ingest/http";

// MQTT. Broker chỉ mở plaintext trên loopback (Mqtt:PlaintextHost = 127.0.0.1), nên muốn ESP32
// trong LAN kết nối được thì chọn một trong hai:
//   a) dev:  Mqtt__PlaintextHost=0.0.0.0 + SR_MQTT_TLS 0  (cổng 1883)
//   b) thật: Mqtt__EnableTls=true + cert  + SR_MQTT_TLS 1  (cổng 8883)
// (SR_MQTT_TLS được khai báo ở đầu file, trước các #include.)
const char*    mqttHost = SR_SERVER_IP;
const uint16_t mqttPort = SR_MQTT_TLS ? 8883 : 1883;

// Credential của board, lấy MỘT LẦN từ response POST /api/v1/devices/claim
// (device sr-u3qb8w ↔ terrarium "Chuong that (ESP32)").
// Vẫn ghi đè được lúc chạy bằng lệnh Serial `set <deviceId> <secret>`.
//
// publicId không phải bí mật nên để làm default; secret thì KHÔNG: nó chỉ tồn tại trong NVS của board.
// Board chưa nạp secret sẽ không gửi gì cả (xem loop()) thay vì nhận 401 lặng lẽ.
#define SR_DEFAULT_DEVICE_ID     "sr-u3qb8w"
#define SR_DEFAULT_DEVICE_SECRET ""

const char* firmwareVersion = "1.0.0";   // tối đa 16 ký tự (rule V-03)
const char* ntpServer       = "pool.ntp.org";

// -------------------------------------------------------------------------
// 3. Chân cảm biến
// -------------------------------------------------------------------------
#define DHTPIN     4      // DHT11 DATA -> GPIO4
#define DHTTYPE    DHT11
#define LM35_PIN   34     // LM35 VOUT -> GPIO34 (ADC1, input-only)
#define I2C_SDA    21
#define I2C_SCL    22

// -------------------------------------------------------------------------
// 4. Nhịp & giới hạn
// -------------------------------------------------------------------------
const uint32_t SAMPLE_INTERVAL_MS    = 12000;  // 5 request/phút (rate limit HTTP: 6/phút/device)
const uint32_t RATE_LIMIT_BACKOFF_MS = 30000;
const uint16_t HTTP_TIMEOUT_MS       = 8000;

// Biên hợp lý lấy đúng từ MetricDictionary.cs. Ngoài biên thì bỏ metric khỏi batch: gửi giá trị vô lý
// lên sẽ khiến server gắn cờ Implausible cho cả sample và loại luôn các metric còn tốt.
const float TF_MIN = -10.0f, TF_MAX = 60.0f;
const float RH_MIN = 0.0f,   RH_MAX = 100.0f;
const float LUX_MIN = 0.0f,  LUX_MAX = 200000.0f;
const float ST_MIN = -10.0f, ST_MAX = 80.0f;

// Attenuation 11 dB (~3,1 V full scale) — đủ dải cho LM35.
// LUÔN dùng `ADC_11db`: đây là enum riêng của Arduino-ESP32 (`adc_attenuation_t` =
// {ADC_0db, ADC_2_5db, ADC_6db, ADC_11db, ADC_ATTENDB_MAX}) và giữ nguyên tên ở cả core 2.x lẫn 3.x.
// Đừng đổi sang `ADC_ATTEN_DB_11`/`ADC_ATTEN_DB_12`: đó là enum `adc_atten_t` của ESP-IDF, core 3.x đã
// bỏ `ADC_ATTEN_DB_11` và `analogSetPinAttenuation()` không nhận kiểu đó.
#define SR_ADC_ATTEN ADC_11db

BH1750 lightMeter;
DHT dht(DHTPIN, DHTTYPE);
Preferences prefs;

#if SR_MQTT_TLS
  WiFiClientSecure net;
#else
  WiFiClient net;
#endif
PubSubClient mqtt(net);

String deviceId     = SR_DEFAULT_DEVICE_ID;
String deviceSecret = SR_DEFAULT_DEVICE_SECRET;
uint32_t sequence   = 1;

// =========================================================================
// CẤU TRÚC DỮ LIỆU MỘT LẦN ĐO
// =========================================================================
struct SensorValues {
  bool  hasTf = false;  float tf = 0, tfRaw = 0;   // nhiệt độ không khí (DHT11)
  bool  hasRh = false;  float rh = 0;              // độ ẩm (DHT11)
  bool  hasLux = false; float lux = 0;             // ánh sáng (BH1750)
  bool  hasSt = false;  float st = 0, stRaw = 0;   // nhiệt độ bề mặt (LM35)
};

// =========================================================================
// NVS: credential + sequence
// =========================================================================
void saveSequence() {
  prefs.putULong("seq", sequence);
}

void loadSettings() {
  prefs.begin("smartreptile", false);

  deviceId     = prefs.getString("deviceId", SR_DEFAULT_DEVICE_ID);
  deviceSecret = prefs.getString("secret", SR_DEFAULT_DEVICE_SECRET);
  ssid         = prefs.getString("ssid", "");
  password     = prefs.getString("wifiPass", "");
  sequence     = prefs.getULong("seq", 0);

  // Một sample có thể đã được server lưu trước khi mất điện nhưng NVS chưa kịp cập nhật.
  // Nhảy qua một giá trị để không gửi lại seq cũ: server coi (DeviceId, Sequence) là khoá dedupe,
  // gửi trùng thì batch bị tính là duplicate và dữ liệu mất im lặng.
  sequence += 1;
  saveSequence();
}

void saveCredentials(const String& id, const String& secret) {
  deviceId = id;
  deviceSecret = secret;
  prefs.putString("deviceId", id);
  prefs.putString("secret", secret);
}

void saveWifi(const String& newSsid, const String& newPassword) {
  ssid = newSsid;
  password = newPassword;
  prefs.putString("ssid", newSsid);
  prefs.putString("wifiPass", newPassword);
}

// =========================================================================
// NHẬP CREDENTIAL QUA SERIAL:  setwifi <ssid> <password> | set <deviceId> <secret> | show
// =========================================================================
void handleSerialCommand() {
  if (!Serial.available()) {
    return;
  }

  String line = Serial.readStringUntil('\n');
  line.trim();

  if (line.startsWith("set ")) {
    int split = line.indexOf(' ', 4);
    if (split < 0) {
      Serial.println(F("[SET] Cú pháp: set <deviceId> <secret>"));
      return;
    }

    String id  = line.substring(4, split);
    String sec = line.substring(split + 1);
    sec.trim();

    if (id.length() == 0 || sec.length() == 0) {
      Serial.println(F("[SET] deviceId/secret rỗng, bỏ qua."));
      return;
    }

    saveCredentials(id, sec);
    Serial.println(F("[SET] Đã lưu credential vào NVS."));
    Serial.println("      deviceId = " + deviceId);
    Serial.println(F("      secret   = ********"));
  } else if (line.startsWith("setwifi ")) {
    // `<ssid> <password>` tách ở dấu cách đầu tiên: SSID/mật khẩu có dấu cách chưa hỗ trợ.
    int split = line.indexOf(' ', 8);
    if (split < 0) {
      Serial.println(F("[SET] Cú pháp: setwifi <ssid> <password>"));
      return;
    }

    String newSsid = line.substring(8, split);
    String newPass = line.substring(split + 1);
    newPass.trim();

    if (newSsid.length() == 0) {
      Serial.println(F("[SET] SSID rỗng, bỏ qua."));
      return;
    }

    saveWifi(newSsid, newPass);
    Serial.println(F("[SET] Đã lưu Wi-Fi vào NVS."));
    Serial.println("      ssid     = " + ssid);
    Serial.println(F("      password = ********"));
    WiFi.disconnect();          // để lượt loop() kế tiếp kết nối lại bằng credential mới
  } else if (line == "show") {
    Serial.println("      ssid     = " + (ssid.length() > 0 ? ssid : String("<chưa nạp>")));
    Serial.println("      deviceId = " + deviceId);
    Serial.println("      secret   = " + String(deviceSecret.length()) + " ký tự");
    Serial.println("      seq      = " + String(sequence));
  } else if (line.length() > 0) {
    Serial.println(F("[SET] Lệnh: setwifi <ssid> <password> | set <deviceId> <secret> | show"));
  }
}

// =========================================================================
// THỜI GIAN: chỉ gửi khi đồng hồ đã đồng bộ NTP
// =========================================================================
// Không dùng mốc thời gian giả: `ts` sai làm server gắn cờ Backfilled/ClockUnsynced
// hoặc kẹp RecordedAt về ReceivedAt (rule V-07…V-09), tức là dữ liệu lẫn vào lịch sử.
bool ensureTime() {
  struct tm timeinfo;

  if (getLocalTime(&timeinfo, 0)) {
    return true;
  }

  configTime(0, 0, ntpServer);   // GMT+0 để ra ISO-8601 UTC
  Serial.println(F("[TIME] Đang đồng bộ NTP..."));
  return getLocalTime(&timeinfo, 5000);
}

String getIsoTimestamp() {
  struct tm timeinfo;

  if (!getLocalTime(&timeinfo, 0)) {
    return String();
  }

  char buffer[25];
  strftime(buffer, sizeof(buffer), "%Y-%m-%dT%H:%M:%SZ", &timeinfo);
  return String(buffer);
}

// =========================================================================
// Wi-Fi
// =========================================================================
bool ensureWifi() {
  if (WiFi.status() == WL_CONNECTED) {
    return true;
  }

  if (ssid.length() == 0) {
    Serial.println(F("[WIFI] Chưa có SSID — nhập: setwifi <ssid> <password>"));
    return false;
  }

  Serial.print(F("[WIFI] Kết nối lại tới '"));
  Serial.print(ssid);
  Serial.print(F("'"));

  WiFi.disconnect();
  WiFi.begin(ssid.c_str(), password.c_str());

  for (int i = 0; i < 30 && WiFi.status() != WL_CONNECTED; i++) {
    delay(500);
    Serial.print('.');
  }
  Serial.println();

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("  -> IP: " + WiFi.localIP().toString());
    return true;
  }

  Serial.println(F("  -> [LỖI] Chưa kết nối được Wi-Fi."));
  return false;
}

// =========================================================================
// ĐỌC CẢM BIẾN
// =========================================================================
void readLux(SensorValues& v) {
  float lux = lightMeter.readLightLevel();   // < 0 khi lỗi I2C/overflow

  if (isnan(lux) || lux < LUX_MIN || lux > LUX_MAX) {
    Serial.printf("[CẢNH BÁO] BH1750 trả về %.1f — bỏ metric lux.\n", lux);
    return;
  }

  v.hasLux = true;
  v.lux = lux;
}

void readDht(SensorValues& v) {
  // DHT11 cần >= 1 s giữa hai lần đọc; đọc liền nhau dễ trả NaN hoặc số cũ.
  float tf = dht.readTemperature();
  delay(1100);
  float rh = dht.readHumidity();

  if (!isnan(tf) && tf >= TF_MIN && tf <= TF_MAX) {
    v.hasTf = true;
    v.tf = tf;
    v.tfRaw = tf;
  } else {
    Serial.println(F("[CẢNH BÁO] DHT11 không đọc được nhiệt độ — bỏ metric tf."));
  }

  if (!isnan(rh) && rh >= RH_MIN && rh <= RH_MAX) {
    v.hasRh = true;
    v.rh = rh;
  } else {
    Serial.println(F("[CẢNH BÁO] DHT11 không đọc được độ ẩm — bỏ metric rh."));
  }
}

void readSurfaceTemp(SensorValues& v) {
  const int samples = 10;
  long totalMilliVolts = 0;
  int lastMilliVolts = 0;

  for (int i = 0; i < samples; i++) {
    lastMilliVolts = analogReadMilliVolts(LM35_PIN);
    totalMilliVolts += lastMilliVolts;
    delay(10);
  }

  float avgMilliVolts = (float)totalMilliVolts / samples;
  float st = avgMilliVolts / 10.0f;        // LM35: 10 mV = 1 °C
  float stRaw = lastMilliVolts / 10.0f;

  if (isnan(st) || st < ST_MIN || st > ST_MAX) {
    Serial.printf("[CẢNH BÁO] LM35 trả về %.2f °C — bỏ metric st.\n", st);
    return;
  }

  v.hasSt = true;
  v.st = st;
  v.stRaw = stRaw;
}

void readSensors(SensorValues& v) {
  readDht(v);
  readLux(v);
  readSurfaceTemp(v);
}

bool hasAnyValue(const SensorValues& v) {
  // Một batch không có metric nào sẽ bị server từ chối `schema_invalid`
  // ("A sample carries no metric this build knows about"), nên không gửi.
  return v.hasTf || v.hasRh || v.hasLux || v.hasSt;
}

// =========================================================================
// ĐÓNG GÓI JSON THEO §3.2
// =========================================================================
void appendPair(String& target, const char* key, const String& value) {
  if (target.length() > 0) {
    target += ',';
  }
  target += '"';
  target += key;
  target += "\":";
  target += value;
}

String buildPayload(const SensorValues& v, const String& timestamp, uint32_t seq) {
  String metrics;

  if (v.hasTf)  appendPair(metrics, "tf",  String(v.tf, 2));
  if (v.hasRh)  appendPair(metrics, "rh",  String(v.rh, 2));
  if (v.hasLux) appendPair(metrics, "lux", String(v.lux, 1));
  if (v.hasSt)  appendPair(metrics, "st",  String(v.st, 2));

  String raw;
  if (v.hasTf)  appendPair(raw, "tf",  String(v.tfRaw, 2));
  if (v.hasLux) appendPair(raw, "lux", String(v.lux, 1));
  if (v.hasSt)  appendPair(raw, "st",  String(v.stRaw, 2));

  String payload;
  payload.reserve(384);

  payload += "{\"deviceId\":\"";  payload += deviceId;
  payload += "\",\"seq\":";        payload += String(seq);
  payload += ",\"fw\":\"";         payload += firmwareVersion;
  payload += "\",\"ts\":\"";       payload += timestamp;
  payload += "\",\"samples\":[{\"t\":0,";
  payload += metrics;

  if (raw.length() > 0) {
    payload += ",\"raw\":{";
    payload += raw;
    payload += '}';
  }
  payload += "}]";

  payload += ",\"health\":{\"rssi\":";  payload += String(WiFi.RSSI());
  payload += ",\"up_s\":";              payload += String((unsigned long)(millis() / 1000UL));
  payload += ",\"heap_kb\":";           payload += String((unsigned long)(ESP.getFreeHeap() / 1024));
  payload += ",\"bat\":null,\"src\":\"mains\"}";
  payload += '}';

  return payload;
}

// =========================================================================
// MQTT (transport chính)
// =========================================================================
String topicTelemetry() {
  return "sr/v1/d/" + deviceId + "/telemetry";
}

String topicStatus() {
  return "sr/v1/d/" + deviceId + "/status";
}

String mqttClientId() {
  return "esp32-" + String((uint32_t)ESP.getEfuseMac(), HEX);
}

void publishStatus(const char* status) {
  String payload = "{\"deviceId\":\"" + deviceId + "\",\"status\":\"";
  payload += status;
  payload += "\",\"fw\":\"";
  payload += firmwareVersion;
  payload += "\"";

  String timestamp = getIsoTimestamp();
  if (timestamp.length() > 0) {
    payload += ",\"at\":\"";
    payload += timestamp;
    payload += "\"";
  }
  payload += "}";

  // Retain = true để LWT "offline" còn hiệu lực khi board mất điện đột ngột (§3.3).
  mqtt.publish(topicStatus().c_str(), payload.c_str(), true);
}

bool ensureMqtt() {
  if (mqtt.connected()) {
    return true;
  }

  String will = "{\"deviceId\":\"" + deviceId + "\",\"status\":\"offline\",\"fw\":\"";
  will += firmwareVersion;
  will += "\"}";

  Serial.print(F("[MQTT] Kết nối tới broker..."));

  bool ok = mqtt.connect(
      mqttClientId().c_str(),
      deviceId.c_str(),
      deviceSecret.c_str(),
      topicStatus().c_str(),
      0,
      true,
      will.c_str());

  if (ok) {
    Serial.println(F(" OK"));
    publishStatus("online");
  } else {
    Serial.println(" THẤT BẠI, state=" + String(mqtt.state()));
  }

  return ok;
}

bool publishTelemetry(const String& payload) {
  bool ok = mqtt.publish(topicTelemetry().c_str(), payload.c_str());

  if (!ok) {
    Serial.println(F("[MQTT] Publish thất bại — chuyển sang HTTPS fallback."));
  }

  return ok;
}

// =========================================================================
// HTTPS FALLBACK (§3.6)
// =========================================================================
int postBatch(const String& payload, uint32_t seq, String& responseBody) {
  HTTPClient http;
  http.setTimeout(HTTP_TIMEOUT_MS);
  http.setReuse(false);            // tránh dùng lại keep-alive đã chết sau khi Wi-Fi rớt

  if (!http.begin(apiUrl)) {
    Serial.println(F("[LỖI] Không mở được kết nối tới server."));
    return -1;
  }

  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", "Device " + deviceId + "." + deviceSecret);
  http.addHeader("Idempotency-Key", deviceId + ":" + String(seq));

  int code = http.POST(payload);

  if (code > 0) {
    responseBody = http.getString();
  } else {
    responseBody = http.errorToString(code);
  }

  http.end();
  return code;
}

// =========================================================================
// SETUP
// =========================================================================
void setup() {
  Serial.begin(115200);
  delay(300);

  Wire.begin(I2C_SDA, I2C_SCL);

  analogReadResolution(12);
  analogSetPinAttenuation(LM35_PIN, SR_ADC_ATTEN);

  Serial.println(F("\n=================================================="));
  Serial.println(F("   SMARTREPTILE TELEMETRY NODE (ESP32)             "));
  Serial.println(F("=================================================="));

  loadSettings();
  Serial.println("  deviceId = " + deviceId);
  Serial.println(F("  (nạp credential: setwifi <ssid> <password> | set <deviceId> <secret>)"));

  if (deviceSecret.length() == 0) {
    Serial.println(F("  [!] Chưa có device secret — board sẽ không gửi gì cho tới khi nạp."));
    Serial.println(F("      Secret chỉ trả về một lần ở response claim / claim-by-mac."));
  }

  if (lightMeter.begin(BH1750::CONTINUOUS_HIGH_RES_MODE)) {
    Serial.println(F("  -> BH1750: Sẵn sàng"));
  } else {
    Serial.println(F("  -> [LỖI] Không thấy BH1750 (SCL=22 / SDA=21)"));
  }

  dht.begin();
  Serial.println(F("  -> DHT11 : Sẵn sàng"));

  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);            // giữ kết nối ổn định khi POST/publish
  ensureWifi();

  ensureTime();
  Serial.println("  -> Thời gian UTC: " + getIsoTimestamp());

#if SR_MQTT_TLS
  net.setInsecure();               // demo: bỏ qua kiểm tra chứng chỉ; production phải nạp CA
  Serial.println(F("  -> MQTT: TLS 8883 (chưa xác thực CA)"));
#else
  Serial.println(F("  -> MQTT: plaintext 1883"));
#endif

  mqtt.setServer(mqttHost, mqttPort);
  mqtt.setBufferSize(512);         // payload telemetry ~300 B > mặc định 256 B
  mqtt.setKeepAlive(30);
  mqtt.setSocketTimeout(5);

  Serial.println(F("==================================================\n"));

  delay(2000);                     // DHT11 cần thời gian ổn định sau khi cấp nguồn
}

// =========================================================================
// LOOP
// =========================================================================
void loop() {
  handleSerialCommand();

  // Không có secret thì mọi batch sẽ bị 401: đứng yên và nhắc nạp thay vì gửi trong vô vọng.
  if (deviceSecret.length() == 0) {
    Serial.println(F("[SET] Chưa có device secret — nhập: set <deviceId> <secret>"));
    delay(3000);
    return;
  }

  uint32_t startedAt = millis();
  uint32_t nextDelay = SAMPLE_INTERVAL_MS;

  if (!ensureWifi() || !ensureTime()) {
    delay(5000);
    return;
  }

  mqtt.loop();

  String timestamp = getIsoTimestamp();
  if (timestamp.length() == 0) {
    Serial.println(F("[TIME] Chưa có giờ UTC, bỏ qua lượt gửi này."));
    delay(5000);
    return;
  }

  SensorValues values;
  readSensors(values);

  if (!hasAnyValue(values)) {
    Serial.println(F("[SKIP] Không cảm biến nào đọc được — không gửi batch rỗng."));
    delay(SAMPLE_INTERVAL_MS);
    return;
  }

  Serial.println(F("--- DỮ LIỆU ĐO ---"));
  if (values.hasTf)  Serial.printf("  tf  : %.2f °C\n", values.tf);
  if (values.hasRh)  Serial.printf("  rh  : %.2f %%\n", values.rh);
  if (values.hasLux) Serial.printf("  lux : %.1f lx\n", values.lux);
  if (values.hasSt)  Serial.printf("  st  : %.2f °C\n", values.st);

  String payload = buildPayload(values, timestamp, sequence);

  // MQTT là đường chính; broker không tới được thì rơi xuống HTTPS fallback.
  bool delivered = ensureMqtt() && publishTelemetry(payload);
  int code = 202;
  String response;

  if (delivered) {
    Serial.println("[MQTT] published " + topicTelemetry() + " seq=" + String(sequence));
  } else {
    code = postBatch(payload, sequence, response);
  }

  if (code == 202) {
    if (!delivered) {
      Serial.println("[OK] 202 " + response);
    }
    sequence += 1;
    saveSequence();
  } else if (code == 429) {
    // Rate limit 6 req/phút/device — lùi lại lâu hơn nhịp thường.
    Serial.println(F("[429] Bị giới hạn tần suất, lùi 30 s."));
    nextDelay = RATE_LIMIT_BACKOFF_MS;
  } else if (code == 401) {
    // Sai/thiếu credential: giữ nguyên seq để sample này vẫn gửi được sau khi nạp lại secret.
    Serial.println("[401] Credential bị từ chối. " + response);
    Serial.println(F("      Kiểm tra deviceId/secret (lệnh: set <deviceId> <secret>)."));
  } else if (code >= 400 && code < 500) {
    // Payload/thiết bị sai: gửi lại y nguyên sẽ hỏng y nguyên (contract §3.6) -> bỏ sample này.
    Serial.printf("[%d] Batch bị từ chối, bỏ sample này. %s\n", code, response.c_str());
    sequence += 1;
    saveSequence();
  } else {
    // 5xx hoặc lỗi transport: giữ seq, gửi lại đúng sample đó ở lượt sau (server dedupe theo seq).
    Serial.printf("[LỖI] Không gửi được (code=%d): %s\n", code, response.c_str());
  }

  uint32_t elapsed = millis() - startedAt;
  if (elapsed < nextDelay) {
    delay(nextDelay - elapsed);
  }
}
