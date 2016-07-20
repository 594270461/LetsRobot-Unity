// vim:set sw=4 ts=4 ai et:

/*
   Arduino sketch for Telly
   (c) Copyright 2016 Let's Robot.
*/

#include <Servo.h>
#include <Adafruit_NeoPixel.h>
#include <i2c_t3.h>
#include <Encoder.h>

#include "library.h"
#include "config.h"

/***/

#define GRIPPER_PIN     9
#define NEOPIXEL_PIN    10
#define PAN_PIN 3

int8_t PWMApin = 23;   //PWM Pin Controlling Motor A
int8_t INA1pin = 16;   //PIN Controlling Motor A output 1
int8_t INA2pin = 17;   //PIN Controlling Motor A output 2
int8_t ENC1Apin = 7;   //Motor 1 Encoder A Pin
int8_t ENC1Bpin = 8;   //Motor 1 Encoder B Pin

int8_t PWMBpin = 22;   //PWM Pin Controlling Motor A
int8_t INB1pin = 21;   //PIN Controlling Motor A output 1
int8_t INB2pin = 20;   //PIN Controlling Motor A output 2
int8_t ENC2Apin = 11;   //Motor 2 Encoder A Pin
int8_t ENC2Bpin = 12;   //Motor 2 Encoder B Pin

int8_t pushButton = 14;

Encoder EncA(ENC1Apin, ENC1Bpin);
Encoder EncB(ENC2Apin, ENC2Bpin);



uint8_t cRate = 20;          // Rate at which control loop runs
uint32_t prevMillis = 0;    // Previous Time in ms

int32_t setpointA = 3576/2  / cRate;   // Desired Motor Velocity (Ticks/Second)
int32_t setpointB = 3576/2  / cRate;   // Desired Motor Velocity (Ticks/Second)
int32_t TickswantedA = 0;  // Desired number of ticks for the motor to travel (1 rev = 3576)
int32_t TickswantedB = 0;  // Desired number of ticks for the motor to travel (1 rev = 3576)

  uint8_t ease = 5;

uint8_t stallA = 0;
uint8_t stallB = 0;

bool posAdone = false;
bool posBdone = false;

uint8_t KP = 2;              // Propoprtional Gain Constant
uint8_t KI = 4;              // Integral Gain Constant
int32_t  oldPosA  = 0;         // Stores Last Encoder Reading;
int32_t  oldPosB  = 0;         // Stores Last Encoder Reading;

bool grip_open = false;

/*
   Continuous rotation servos for the two wheels.  Because of the way the
   servos had to be mounted, they're facing different directions.  With this
   library, 90 is the stop value, 0 is full-speed in one direction, and 180
   is full-speed in the other direction.
*/

int dspeed = 10;
int tspeed = 10;
int default_drive_time = 1000;
int default_turn_time = 250;


Servo gripper_servo;
Servo pan_servo;

/*
   How long in milliseconds to drive the motors when moving forward and
   backward, and left and right.
*/
#define MAX_DRIVE_TIME      3000
#define MAX_TURN_TIME       3000
//#define DEFAULT_DRIVE_TIME   750
//#define DEFAULT_TURN_TIME    500

/*
   I2C, for supporting the old protocol's LED interface
*/
#define I2C_ADDRESS      0x04
#define NUM_LEDS         (9*2)
#define I2C_TIMEOUT_TIME 1000

int gripper_open = 180;
int gripper_close = 0;

/*
   The mapping of LEDs as they appear on the NeoPixel strip, vs the human
   address we'd like to give them.
*/
// mirror (low towards center)
int led_map[NUM_LEDS] = {
  6,  //  0
  7,  //  1
  8,  //  2
  3,  //  3
  4,  //  4
  5,  //  5
  0,  //  6
  1,  //  7
  2,  //  8
  15,  //  9
  16,  // 10
  17,  // 11
  12,  // 12
  13,  // 13
  14,  // 14
  9,  // 15
  10,  // 16
  11,  // 17
};


Adafruit_NeoPixel eyes = Adafruit_NeoPixel(NUM_LEDS, NEOPIXEL_PIN, NEO_GRB + NEO_KHZ800);
int eye_state;
bool eyes_no_show = false;


/***/

/*
   Asyncrhonously scheduled stop.

   Values:
      0   No stop scheduled
      1   Stop immediately
     >1   Stop when millis() is greater than this number
*/
unsigned long stop_time;

void schedule_stop(unsigned long timer) {
  if (timer > 1)
    stop_time = millis() + timer;
  else
    stop_time = timer;
}

void stop(void) {
//  left_servo.write(left_stop);
//  right_servo.write(right_stop);

  /*
     If the value was 1, this is a special case, and the STOP is being
     executed immediately.  Return the normal OK string.  Otherwise, the
     stop is the result of a previously scheduled asynchronous movement
     command.  Return the asynchronous OK  string.
  */

  if (compat) {
    OK();
  }

  else {
    if (stop_time == 1)
      OK();
    else
      redraw_prompt = true;
    Serial.println("\rASYNC_OK");
  }

  stop_time = 0;
}

void move(int left, int right, int default_time) {
  int duration;

  duration = nextarg_int(10, MAX_DRIVE_TIME, default_time, -1);

  if (duration < 0) {
    Serial.println("ASYNC_ERR Invalid argument");
    return;
  }

  if (! compat)
    Serial.println("ASYNC_RUNNING");
//  left_servo.write(left);
//  right_servo.write(right);
  schedule_stop(duration);
}

void heartbeat(void) {
  if (0 < stop_time && stop_time < millis())
    stop();
}

/***/

/*
  void cmd_forward (void) { move(LEFT_FORWARD,  RIGHT_FORWARD,  default_drive_time); }
  void cmd_back    (void) { move(LEFT_BACKWARD, RIGHT_BACKWARD, default_drive_time); }
  void cmd_left    (void) { move(LEFT_BACKWARD, RIGHT_FORWARD,  default_turn_time);  }
  void cmd_right   (void) { move(LEFT_FORWARD,  RIGHT_BACKWARD, default_turn_time);  }
*/

/*
   I2C callbacks, for the old protocol
*/

void sendData() {
  //verbose("I2C write eye_state %d", eye_state);
  Wire.write(eye_state);
}

void receiveData(size_t len) {
  static int R, G, B;
  static int pixel;
  static unsigned long last_read;

  //verbose("I2C receiveDat num_bytes %d", num_bytes);

  /*
     A timeout event, to help avoid out-of-sync errors with the Pi.  If we
     haven't heard anything from the Pi in over half a second, go back to
     state 0, assuming the next byte we're gonig to read is the start of a
     new LED command.
  */
  if (millis() - last_read > I2C_TIMEOUT_TIME)
    eye_state = 0;

  last_read = millis();

  while (Wire.available()) {
    uint8_t val = Wire.read();
    //verbose("I2C read %d", val);

    switch (eye_state) {
      case 0:
        pixel = val;
        eye_state++;
        break;

      case 1:
        R = val;
        eye_state++;
        break;

      case 2:
        G = val;
        eye_state++;
        break;

      case 3:
        B = val;
        eye_state = 0;
        do_command(format("led %d %d %d %d", pixel, R, G, B));
        break;
    }
  }
}

/***/

#define EYE_BLINK_COLOR "20 10 0"

void blink_open() {
  eyes_no_show = true;
  do_command("led 255 " EYE_BLINK_COLOR);
  do_command("led   5 " "0 0 0");
  do_command("led  14 " "0 0 0");
  eyes_no_show = false;
  eyes_show();
}

void blink_close () {
  eyes_no_show = true;
  do_command("led 255 0 0 0");

  do_command("led   4 " EYE_BLINK_COLOR);
  do_command("led   5 " EYE_BLINK_COLOR);
  do_command("led   6 " EYE_BLINK_COLOR);

  do_command("led  13 " EYE_BLINK_COLOR);
  do_command("led  14 " EYE_BLINK_COLOR);
  do_command("led  15 " EYE_BLINK_COLOR);
  eyes_no_show = false;
  eyes_show();
}

/***/

void eyes_show() {
  if (eyes_no_show)
    return;
//  servo_detach();
  eyes.show();
//  servo_attach();
}

void servo_attach() {
//  left_servo.attach(LEFT_PIN);
//  right_servo.attach(RIGHT_PIN);
 gripper_servo.attach(GRIPPER_PIN);
  pan_servo.attach(PAN_PIN);
  //pan_servo.attach(12);
  // was 6, now 12
  //tilt_servo.attach(11);
}

void servo_detach() {
//  left_servo.detach();
//  right_servo.detach();
//  gripper_servo.detach();
  //pan_servo.detach();
  //tilt_servo.detach();
}

void setup() {
  add_command("forward",  cmd_forward);
  add_command("back",     cmd_back);
  add_command("backward", cmd_back);
  add_command("left",     cmd_left);
  add_command("right",    cmd_right);
  add_command("stop",     cmd_stop);
  add_command("f",        cmd_forward);
  add_command("b",        cmd_back);
  add_command("l",        cmd_left);
  add_command("r",        cmd_right);
  add_command("s",        cmd_stop);
  add_command("led",      cmd_led);
  add_command("blink",    cmd_blink);

  add_command("setdt",    cmd_setdt);
  add_command("settt",    cmd_settt);
  add_command("setds",    cmd_setds);
  add_command("setts",    cmd_setts);

  add_command("setgo",    cmd_setgo);
  add_command("setgc",    cmd_setgc);
  add_command("open",    cmd_open);
  add_command("close",    cmd_close);
  add_command("tics", cmd_ticks);
  add_command("setv", cmd_setv);
  add_command("speed", cmd_setv);
  add_command("pan",     cmd_pan);
  add_command("grab",     cmd_grab);
  add_command("angle", cmd_r_ticks);
  add_command("tilt",    cmd_ticks);
  add_command("detach", cmd_grip_detach);
  add_command("attach", cmd_grip_attach);
  

  Serial.begin(19200);

  delay(1000);
  Serial.println("Check1");

  /*
     For backwards compatability with the old protocol, only, which used I2C
     to control the LEDs.

     Become an I2C slave.  First change the bitrate to 400khz, which must be
     set before calling Wire.begin().  Then set the I2C callback functions.
  */
     Wire.begin(I2C_SLAVE, I2C_ADDRESS, I2C_PINS_18_19, I2C_PULLUP_EXT, I2C_RATE_400);
     Wire.onReceive(receiveData);
     Wire.onRequest(sendData);
  
      eyes.begin();
      eyes_show();
  

  
      servo_attach();
      cmd_open();
//  
//      pinMode(LED_BUILTIN, OUTPUT);
//  
//      for (int i = 0; i < 8; i++) {
//          digitalWrite(LED_BUILTIN, LOW);
//          delay(33);
//  
//          digitalWrite(LED_BUILTIN, HIGH);
//          delay(33);
//      }

  delay(1000);
  //do_command("pan 85");
  //do_command("tilt 120");

  cmd_blink();
  Motor_Setup(PWMApin, INA1pin, INA2pin);
  Motor_Setup(PWMBpin, INB1pin, INB2pin); 
  analogWrite(PWMApin, 0);
  analogWrite(PWMBpin, 0);
  delay(1000);
  Serial.println("check2");
  pinMode(LED_BUILTIN,OUTPUT); // LED
  digitalWrite(LED_BUILTIN,HIGH);
  pinMode(pushButton, INPUT);
  digitalWrite(pushButton,HIGH);
}

void loop() {
  library_loop();
  
}