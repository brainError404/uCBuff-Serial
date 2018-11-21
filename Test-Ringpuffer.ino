#include <stdio.h>
#include <stdlib.h>
#include <inttypes.h>

const int dataLength = 7;

volatile byte cSIndex = 99;

int dgToRead = 1;                           // determines how many datagrams per intervall should be read to regulate the buffers capacity
int bytesAvailable = 0;                     // how many datagrams are ready to read from serial
int dgToReadNow = 1;                        // how many datagrams will be read this cycle from serial

byte telBuffFull[] = {'b', 77, 77, 77, 77, 77, 77};
byte telBuffEmpty[] = {'b', 1, 1, 1, 1, 1, 1};
byte telTest[] = {'t', 88, 88, 88, 88, 88, 88};

enum states {
  PREPARE_BUF,
  MOVING,
  PAUSED
};

int state = PREPARE_BUF;                    // fill the buffer before starting

// ringbuffer
typedef struct
{
  int * const buff;
  int head;
  int tail;
  const int maxLen;
} circBuf_t;

#define CIRCBUF_DEF(x,y)        \
  int x##_space[y];             \
  circBuf_t x = {               \
                                .buff = x##_space,        \
                                .head = 0,                \
                                .tail = 0,                \
                                .maxLen = y               \
                }



CIRCBUF_DEF(buf, 64 );      // Rinbuffer mit 128 Elementen anlegen
const int buffThresLow = (int) (buf.maxLen * 0.2);
const int buffThresHalf = (int) (buf.maxLen * 0.5);
const int buffThresHigh = (int) (buf.maxLen * 0.8);

int push(circBuf_t *c, byte data)
{
  // next is where head will point to after this write.
  byte next = c->head + 1;
  if (next >= c->maxLen) {
    next = 0;
  }


  if (next == c->tail) {    // check if circular buffer is full
    return -1;              // and return with an error.
  }

  c->buff[c->head] = data;  // Load data and then move
  c->head = next;           // head to next data offset.
  return 0;                 // return success to indicate successful push.
}

int pop(circBuf_t *c, byte *data)
{
  // if the head isn't ahead of the tail, we don't have any characters
  if (c->head == c->tail - 1)   // check if circular buffer is empty
    return -1;              // and return with an error



  // next is where tail will point to after this read.
  int next = c->tail + 1;
  if (next >= c->maxLen)
    next = 0;

  *data = c->buff[c->tail]; // Read data and then move
  c->tail = next;           // tail to next data offset.
  return 0;                 // return success to indicate successful push.
}


void setup() {
  Serial.begin(115200);
  digitalWrite(LED_BUILTIN, LOW);

}

int cnt = 0;

byte data = 0;
byte val = 0;

void loop() {

  switch (state) {
    case PREPARE_BUF:
      PrepareBuffer();
      state = MOVING;
      break;

    case MOVING:

      // only send data when we received them and scara should move
      while (state == MOVING) {

        // check capacity of buffer
        if (GetBytesInBuff(buf) < buffThresLow) {
          // notify pc that ringbuffer is going to become empty
          WriteDatagram(telBuffEmpty);
          //dgToRead = 2;
        }
        if (GetBytesInBuff(buf) > buffThresHigh) {
          // notify that buffer is close to full
          WriteDatagram(telBuffFull);
          //dgToRead = 0;
        }

        // pop data from buffer to serial
        for (int i = 0; i < dataLength; i++) {
          pop(&buf, &data);
          // cancel transaction, cause we received the last datagram
          if ((i == 0) &&  (data == 'e')) {

            state = PREPARE_BUF;

            // reset the ring buffer
            buf.head = 0;
            buf.tail = 0;

            break;
          }

          Serial.write(data);

        }

        //GetBuffInfos(telTest, &buf);
        //WriteDatagram(telTest);

        // check if theres enough data in serial rx buffer
        /*bytesAvailable = Serial.available();
          if (bytesAvailable >= dgToRead * 7) {
          // read all the required datagrams this cycle
          dgToReadNow = dgToRead;
          // reset the regulator value
          dgToRead = 1;
          }
          // if theres not enough data available read as much as possible
          // and read the rest next cycle
          else {
          dgToReadNow = bytesAvailable;
          // read next cycle the difference plus the new incoming datagram
          dgToRead = dgToRead - dgToReadNow + 1;

          /*telTest[1] = (byte)bytesAvailable;
          telTest[2] = (byte)dgToRead * 7;
          telTest[3] = (byte)buf.head;
          telTest[4] = (byte)buf.tail;
          telTest[5] = (byte)abs(buf.head - buf.tail);
          telTest[6] = (byte)GetBytesInBuff(buf);
          WriteDatagram(telTest);
          }*/
        dgToReadNow = 1;

        // read another datagramm from serial port
        if (state == MOVING && (dgToRead > 0)) {
          // read more ore less than one datagram if the buffers storage (-> capacity) is to much or less
          for (int i = 0; i < dgToReadNow; i++) {

            if (Serial.available() >= dataLength) {
              for (int j = 0; j < dataLength; j++) {

                // check for pause call sign
                if (j == 0) {
                  val = Serial.read();
                  // pause all action if requested
                  if (val == 'p') {
                    state = PAUSED;
                    break;
                  }
                  else {
                    push(&buf, val);
                  }
                }
                else {
                  push(&buf, Serial.read());
                }
              }
            }
            else {
              // send infos about ring and rx buffer
              telTest[6] = (byte) Serial.available();
              GetBuffInfos(telTest, &buf);
              WriteDatagram(telTest);
            }

          }
        }

        delay(50);
      }
      break;

    case PAUSED:
      WaitForResume();
      state = MOVING;
      break;
  }
}





void WriteDatagram(byte *datagram) {
  for (int i = 0; i < dataLength; i++) {
    Serial.write(*(datagram + i));
  }
}

int GetBytesInBuff(circBuf_t buff) {
  int diff = buff.head - buff.tail;
  diff = (diff < 0) ? buff.maxLen - buff.tail + buff.head : diff;
  return diff;
}

void GetBuffInfos(byte *telBuffEmpty, circBuf_t *buff) {
  static int index = 0;
  *(telBuffEmpty + 1) = (byte)index++;
  *(telBuffEmpty + 2) = (byte)GetBytesInBuff(*buff);
  *(telBuffEmpty + 3) = (byte)buffThresLow;
  *(telBuffEmpty + 4) = (byte)buff->head;
  *(telBuffEmpty + 5) = (byte)buff->tail;
}

// checks if buffer is half full before starting
void PrepareBuffer() {

  int cnt = 0;                                    // byte index of datagram
  bool firstByteValid = false;                    // true if recceived call sign for starting

  // check capacity of buffer after receiving a whole datagram
  while (GetBytesInBuff(buf) < buffThresHalf) {
    // ensure that only whole datagráms are filled into the buffer
    while (cnt < dataLength) {
      // only read data if available
      if (Serial.available()) {
        // only fill buffer if first call sign was valid
        if (firstByteValid) {
          // fill the buffer
          push(&buf, Serial.read());
          cnt++;
        }
        // first call sign must be an 's'
        else if (Serial.read() == 's') {
          firstByteValid = true;
          push(&buf, 's');
          cnt++;
        }
      }
    }
    // reset the datagram index
    cnt = 0;
  }
}

void WaitForResume() {
  digitalWrite(LED_BUILTIN, HIGH);
  while (state == PAUSED) {
    if (Serial.available()) {
      if (Serial.read() == 'p') {
        // read the whole endPause datagram but dont save it
        for (int i = 0; i < (dataLength - 1); i++) {
          while (!Serial.available());
          Serial.read();
        }
        break;
        digitalWrite(LED_BUILTIN, LOW);
      }
    }
  }
}



