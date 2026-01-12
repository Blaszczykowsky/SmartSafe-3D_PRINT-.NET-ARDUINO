#include <Arduino.h>
#include <ESP8266WiFi.h>
#include <WiFiUdp.h>
#include <SPI.h>
#include <MFRC522.h>

const char* ssid = "SEJF_CAM";
const char* pass = "12345678";

WiFiUDP udp;
const uint16_t UDP_PORT = 4210;

IPAddress esp32_ip = WiFi.gatewayIP();

enum Msg : uint8_t {
  MSG_PIN_OK   = 1,
  MSG_RFID_OK  = 2,
  MSG_RFID_BAD = 3
};

static void sendToEsp32(Msg msg) {
  uint8_t b = (uint8_t)msg;
  for (int i = 0; i < 3; i++) {
    udp.beginPacket(esp32_ip, UDP_PORT);
    udp.write(&b, 1);
    udp.endPacket();
    delay(20);
  }
}

#define SS_PIN   D8 
#define RST_PIN  D4 
MFRC522 mfrc522(SS_PIN, RST_PIN);

const byte allowed[][4] = { {0xB5, 0x57, 0xCF, 0x06} };
const int allowedCount = sizeof(allowed)/sizeof(allowed[0]);

bool uidMatches(const byte* uid) {
  for (int i=0;i<allowedCount;i++){
    bool ok=true;
    for(int j=0;j<4;j++){
      if(allowed[i][j]!=uid[j]){ ok=false; break; }
    }
    if(ok) return true;
  }
  return false;
}

void sendEventToEsp32(uint8_t msg) {
  for (int i=0;i<3;i++) {
    udp.beginPacket(esp32_ip, UDP_PORT);
    udp.write(&msg, 1);
    udp.endPacket();
    delay(20);
  }
}

void unlock(const char* reason) {
  Serial.print("ZAMEK OTWARTY (");
  Serial.print(reason);
  Serial.println(")");
}

void handleUdp() {
  int p = udp.parsePacket();
  if (p <= 0) return;

  uint8_t b=0;
  udp.read(&b, 1);
  if (b == MSG_PIN_OK) unlock("PIN z ESP32");
}

void setup() {
  Serial.begin(115200);
  delay(300);

  WiFi.setSleep(false);

  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, pass);
  while (WiFi.status() != WL_CONNECTED) delay(300);
  esp32_ip = WiFi.gatewayIP();
  Serial.print("Gateway (ESP32) = ");
  Serial.print(esp32_ip);

  udp.begin(UDP_PORT);
  Serial.printf("[ESP8266] UDP listen: %u\n", UDP_PORT);

  SPI.begin();
  mfrc522.PCD_Init();
  Serial.println("[RFID] RC522 init OK");
  Serial.println("Przylóż kartę...");
}

void loop() {
  static unsigned long last = 0;
  if (millis() - last > 1000) {
    last = millis();
    WiFi.RSSI();
  }

  handleUdp();

  if (!mfrc522.PICC_IsNewCardPresent()) return;
  if (!mfrc522.PICC_ReadCardSerial()) return;

  Serial.print("UID: ");
  for (byte i=0;i<mfrc522.uid.size;i++) Serial.printf("%02X ", mfrc522.uid.uidByte[i]);
  Serial.println();

  bool ok = (mfrc522.uid.size >= 4) && uidMatches(mfrc522.uid.uidByte);

  if (ok) {
    unlock("RFID");
    sendToEsp32(MSG_RFID_OK);
  } else {
    Serial.println("BRAK DOSTEPU");
    sendToEsp32(MSG_RFID_BAD);
  }

  mfrc522.PICC_HaltA();
  mfrc522.PCD_StopCrypto1();
  delay(300);
}
