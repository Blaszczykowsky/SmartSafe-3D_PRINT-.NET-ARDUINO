#pragma once
#include <Arduino.h>

void PinLogic_begin(const char* authToken);
void PinLogic_tick();
void PinLogic_handleClient();

const char* PinLogic_getCurrentPin();
bool PinLogic_isPinCorrect(const String& entered);
