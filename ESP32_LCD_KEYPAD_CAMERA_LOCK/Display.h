#pragma once
#include <Arduino.h>

void Display_begin();
void Display_prompt();
void Display_showMask(size_t enteredLen);
void Display_result(bool ok);
void Display_showLine(const String& line1, const String& line2);
