#include <Arduino.h>
#include <WiFi.h>
#include "board_config.h"
#include <WiFiUdp.h>

#include "CameraService.h"
#include "Display.h"
#include "KeypadInput.h"
#include "PinLogic.h"
#include "Pins.h"
#include "LockService.h"

static const char *AUTH_TOKEN = "SEJF_SHARED_TOKEN_123";

static String enteredPin;

WiFiUDP udp;
const uint16_t UDP_PORT = 4210;

enum Msg : uint8_t {
  MSG_RFID_OK  = 2,
  MSG_RFID_BAD = 3
};

static void granAccess(const char* reason) {
  Serial.printf("Nadano dostep(%s)", reason);

  if(doorIsOpen()){
    showTemp("DRZWI OTWARTE","RFID OK", 1200);
    return;
  }

  LockService_pulse(LOCK_PULSE_MS);
  showTemp("RFID OK","SEJF OTWARTY", 1200);
}

static void handleUdpFromEsp8266() {
  int p = udp.parsePacket();
  if (p <= 0) return;

  uint8_t b = 0;
  udp.read(&b, 1);

  if (b == MSG_RFID_OK){granAccess("RFID");}
  if (b == MSG_RFID_BAD){showTemp("RFID FAIL","",900);}
}

static inline bool doorIsOpen() {
#if defined(DOOR_SWITCH_ACTIVE_LOW) && (DOOR_SWITCH_ACTIVE_LOW == 1)
  return digitalRead(PIN_DOOR_SWITCH) == LOW;
#else
  return digitalRead(PIN_DOOR_SWITCH) == HIGH;
#endif
}

static uint32_t showUntilMs = 0;
static bool showingResult = false;

static void showTemp(const char* l1, const char* l2, uint16_t ms) {
  Display_showLine(l1, l2);
  showUntilMs = millis() + ms;
  showingResult = true;
}

void setup() {
  Serial.begin(115200);
  Serial.setDebugOutput(true);
  Serial.println();
  delay(300);

  Serial.printf("PSRAM: %s\n", psramFound() ? "OK" : "NOT FOUND");
  randomSeed((uint32_t)esp_random());

  Display_begin();
  Display_prompt();

  pinMode(PIN_DOOR_SWITCH, INPUT_PULLUP);

  LockService_begin(PIN_LOCK_RELAY, LOCK_RELAY_ACTIVE_HIGH);

  if (!CameraService_begin()) {
    Display_showLine("Cam init FAIL", "");
    return;
  }

  WiFi.mode(WIFI_AP);
  WiFi.softAP("SEJF_CAM", "12345678");

  Serial.print("AP IP: ");
  Serial.println(WiFi.softAPIP());

  udp.begin(UDP_PORT);

  CameraService_startServer();

  PinLogic_begin(AUTH_TOKEN);

  Display_prompt();
}

void loop() {
  LockService_tick();

  handleUdpFromEsp8266();

  PinLogic_tick();

  PinLogic_handleClient();

  if (showingResult && (int32_t)(millis() - showUntilMs) >= 0) {
    showingResult = false;
    enteredPin = "";
    Display_prompt();
  }

  char k = KeypadInput_getKey();
  if (!k) {
    delay(2);
    return;
  }

  if (showingResult) {
    showingResult = false;
    showUntilMs = 0;
    enteredPin = "";
    Display_prompt();
    delay(2);
    return;
  }

  if (k == '*') {
    enteredPin = "";
    Display_prompt();
    delay(2);
    return;
  }

  if (k == '#') {
    if (enteredPin.length() != 4) {
      showTemp("PIN za krotki", "", 800);
      enteredPin = "";
      delay(2);
      return;
    }

    bool ok = PinLogic_isPinCorrect(enteredPin);

    Serial.printf("[SAFE] entered=%s current=%s => %s\n",
                  enteredPin.c_str(),
                  PinLogic_getCurrentPin(),
                  ok ? "OK" : "WRONG");

    if (ok) {
      if (doorIsOpen()) {
        showTemp("DRZWI OTWARTE", "ZAMKA NIE TRZEBA", 1200);
      } else {
        LockService_pulse(LOCK_PULSE_MS);
        Display_result(true);
        showingResult = true;
        showUntilMs = millis() + 1200;
      }
    } else {
      Display_result(false);
      showingResult = true;
      showUntilMs = millis() + 1200;
    }

    enteredPin = "";
    delay(2);
    return;
  }

  if (k >= '0' && k <= '9') {
    if (enteredPin.length() < 4) {
      enteredPin += k;
      Display_showMask(enteredPin.length());
    }
  }

  delay(2);
}