#include "PinLogic.h"
#include <WebServer.h>
#include "LockService.h"

static WebServer pinServer(8080);

static const char* g_authToken = nullptr;

static uint32_t PIN_REFRESH_MS = 2UL * 60UL * 1000UL;
static uint32_t lastPinGenMs = 0;
static char currentPin[8] = "0000";

static void generatePin4() {
  int p = (int)random(0, 10000);
  snprintf(currentPin, sizeof(currentPin), "%04d", p);
}

static bool authorized() {
  if (!pinServer.hasHeader("X-Auth")) return false;
  return String(pinServer.header("X-Auth")) == String(g_authToken);
}

static void handlePinJson() {
  if (!authorized()) {
    pinServer.send(401, "application/json", "{\"ok\":false,\"err\":\"unauthorized\"}");
    return;
  }
  String json = String("{\"ok\":true,\"pin\":\"") + currentPin + "\"}";
  pinServer.send(200, "application/json", json);
}

static void handleLockJson() {
  if (!authorized()) {
    pinServer.send(401, "application/json", "{\"ok\":false,\"err\":\"unauthorized\"}");
    return;
  }

  String cmd = pinServer.hasArg("cmd") ? pinServer.arg("cmd") : "";
  cmd.toLowerCase();

  if (cmd == "open") {
    LockService_open();
  } else if (cmd == "close") {
    LockService_close();
  } else if (cmd == "pulse") {
    uint32_t ms = 0;
    if (pinServer.hasArg("ms")) ms = (uint32_t)pinServer.arg("ms").toInt();
    if (ms == 0) ms = 1500;
    LockService_pulse(ms);
  } else if (cmd.length() > 0) {
    pinServer.send(400, "application/json", "{\"ok\":false,\"err\":\"bad_cmd\"}");
    return;
  }

  String json = String("{\"ok\":true,\"state\":\"") + (LockService_isOpen() ? "open" : "closed") + "\"}";
  pinServer.send(200, "application/json", json);
}

static void handleRoot() {
  if (!authorized()) {
    pinServer.send(401, "text/plain", "Unauthorized");
    return;
  }
  String html;
  html += "<!doctype html><html><head><meta charset='utf-8'/>";
  html += "<meta name='viewport' content='width=device-width,initial-scale=1'/>";
  html += "<title>ESP32-CAM</title></head><body style='font-family:Arial'>";
  html += "<h3>OK</h3><p>PIN JSON: /pin</p></body></html>";
  pinServer.send(200, "text/html", html);
}

void PinLogic_begin(const char* authToken) {
  g_authToken = authToken;

  const char* headerKeys[] = {"X-Auth"};
  pinServer.collectHeaders(headerKeys, 1);
  pinServer.on("/", handleRoot);
  pinServer.on("/pin", handlePinJson);
  pinServer.on("/lock", handleLockJson);
  pinServer.begin();

  Serial.println("[PIN] Server started");

  generatePin4();
  lastPinGenMs = millis();
  Serial.printf("[PIN] Initial PIN: %s\n", currentPin);
}

void PinLogic_tick() {
  uint32_t now = millis();
  if (lastPinGenMs == 0 || (now - lastPinGenMs) >= PIN_REFRESH_MS) {
    lastPinGenMs = now;
    generatePin4();
    Serial.printf("[PIN] New PIN: %s\n", currentPin);
  }
}

void PinLogic_handleClient() {
  pinServer.handleClient();
}

const char* PinLogic_getCurrentPin() {
  return currentPin;
}

bool PinLogic_isPinCorrect(const String& entered) {
  return entered == String(currentPin);
}
