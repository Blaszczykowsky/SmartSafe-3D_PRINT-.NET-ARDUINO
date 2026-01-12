#include "Display.h"
#include <Wire.h>
#include <LiquidCrystal_I2C.h>

static constexpr int I2C_SDA = 47;
static constexpr int I2C_SCL = 42;

static LiquidCrystal_I2C lcd(0x27, 16, 2);

void Display_begin() {
  Wire.begin(I2C_SDA, I2C_SCL);
  Wire.setTimeOut(50);
  lcd.init();
  lcd.backlight();
}

void Display_prompt() {
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print("Wpisz PIN:");
  lcd.setCursor(0, 1);
  lcd.print("____");
}

void Display_showMask(size_t enteredLen) {
  lcd.setCursor(0, 1);

  String m;
  for (size_t i = 0; i < enteredLen; i++) m += '*';
  while (m.length() < 4) m += '_';

  lcd.print(m);
  for (int i = (int)m.length(); i < 16; i++) lcd.print(' ');
}

void Display_result(bool ok) {
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(ok ? "PIN OK" : "ZLY PIN");
  lcd.setCursor(0, 1);
  lcd.print(ok ? "SEJF ODBLOK." : "SPROBUJ PON.");
  delay(900);
  Display_prompt();
}

void Display_showLine(const String& line1, const String& line2) {
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(line1);
  lcd.setCursor(0, 1);
  lcd.print(line2);
}
