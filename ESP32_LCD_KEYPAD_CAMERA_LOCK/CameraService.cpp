#include "CameraService.h"
#include "esp_camera.h"
#include "board_config.h"

void startCameraServer();
void setupLedFlash();

static bool cam_inited = false;
static bool server_started = false;

bool CameraService_begin() {
  if (cam_inited) return true;

  camera_config_t config = {};
  config.ledc_channel = LEDC_CHANNEL_0;
  config.ledc_timer = LEDC_TIMER_0;

  config.pin_d0 = Y2_GPIO_NUM;
  config.pin_d1 = Y3_GPIO_NUM;
  config.pin_d2 = Y4_GPIO_NUM;
  config.pin_d3 = Y5_GPIO_NUM;
  config.pin_d4 = Y6_GPIO_NUM;
  config.pin_d5 = Y7_GPIO_NUM;
  config.pin_d6 = Y8_GPIO_NUM;
  config.pin_d7 = Y9_GPIO_NUM;

  config.pin_xclk = XCLK_GPIO_NUM;
  config.pin_pclk = PCLK_GPIO_NUM;
  config.pin_vsync = VSYNC_GPIO_NUM;
  config.pin_href = HREF_GPIO_NUM;

  config.pin_sccb_sda = SIOD_GPIO_NUM;
  config.pin_sccb_scl = SIOC_GPIO_NUM;

  config.pin_pwdn = PWDN_GPIO_NUM;
  config.pin_reset = RESET_GPIO_NUM;

  config.pixel_format = PIXFORMAT_JPEG;

  config.frame_size = FRAMESIZE_QVGA;
  config.jpeg_quality = 22;

  if (psramFound()) {
    config.fb_location = CAMERA_FB_IN_PSRAM;
    config.fb_count = 2;
  } else {
    config.fb_location = CAMERA_FB_IN_DRAM;
    config.fb_count = 1;
  }

  config.grab_mode = CAMERA_GRAB_LATEST;
  config.xclk_freq_hz = 20000000;
  
  esp_err_t err = esp_camera_init(&config);
  if (err != ESP_OK) {
    Serial.printf("[CAM] init FAILED: 0x%x\n", err);
    return false;
  }

sensor_t *s = esp_camera_sensor_get();
if(s){
  s->set_hmirror(s,1);
  s->set_vflip(s,1);

  s->set_whitebal(s,1);
  s->set_awb_gain(s,1);
  s->set_gain_ctrl(s,1);
  s->set_exposure_ctrl(s,1);
}

#if defined(LED_GPIO_NUM)
  setupLedFlash();
#endif

  cam_inited = true;
  Serial.println("[CAM] init OK");
  return true;
}

void CameraService_startServer() {
  if (!cam_inited) {
    Serial.println("[CAM] startServer called before init!");
    return;
  }
  if (server_started) return;

  startCameraServer();
  server_started = true;

  Serial.println("[CAM] Camera server started (80/81)");
}
