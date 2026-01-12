#include "KeypadInput.h"
#include <Keypad.h>

static const byte ROWS = 4;
static const byte COLS = 3;

static byte rowPins[ROWS] = {2, 1, 38, 39};
static byte colPins[COLS] = {21, 40, 41};

char keys[ROWS][COLS] = {
  {'1','2','3'},
  {'4','5','6'},
  {'7','8','9'},
  {'*','0','#'}
};

static Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);

char KeypadInput_getKey() {
  return keypad.getKey();
}
