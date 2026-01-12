#include "LockService.h"

static int g_gpio = -1;
static bool g_activeHigh = true;
static bool g_open = false;

static bool g_pulseActive = false;
static uint32_t g_pulseUntilMs = 0;

static void writeRelay(bool open) {
  if (g_gpio < 0) return;
  g_open = open;
  bool level = g_activeHigh ? open : !open;
  digitalWrite(g_gpio, level ? HIGH : LOW);
}

void LockService_begin(int gpio, bool activeHigh) {
  g_gpio = gpio;
  g_activeHigh = activeHigh;
  pinMode(g_gpio, OUTPUT);
  writeRelay(false);
  g_pulseActive = false;
  g_pulseUntilMs = 0;
  Serial.printf("[LOCK] gpio=%d activeHigh=%d\n", g_gpio, (int)g_activeHigh);
}

void LockService_open() {
  g_pulseActive = false;
  writeRelay(true);
  Serial.println("[LOCK] OPEN");
}

void LockService_close() {
  g_pulseActive = false;
  writeRelay(false);
  Serial.println("[LOCK] CLOSE");
}

void LockService_pulse(uint32_t ms) {
  if (ms < 100) ms = 100;
  if (ms > 10000) ms = 10000;
  g_pulseActive = true;
  g_pulseUntilMs = millis() + ms;
  writeRelay(true);
  Serial.printf("[LOCK] PULSE %lu ms\n", (unsigned long)ms);
}

void LockService_tick() {
  if (!g_pulseActive) return;
  if ((int32_t)(millis() - g_pulseUntilMs) >= 0) {
    g_pulseActive = false;
    writeRelay(false);
    Serial.println("[LOCK] PULSE END -> CLOSE");
  }
}

bool LockService_isOpen() {
  return g_open;
}
