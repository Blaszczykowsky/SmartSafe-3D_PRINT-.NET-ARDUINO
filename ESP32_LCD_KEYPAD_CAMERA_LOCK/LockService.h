#pragma once

#include <Arduino.h>

void LockService_begin(int gpio, bool activeHigh);
void LockService_open();
void LockService_close();
void LockService_pulse(uint32_t ms);
void LockService_tick();
bool LockService_isOpen();
