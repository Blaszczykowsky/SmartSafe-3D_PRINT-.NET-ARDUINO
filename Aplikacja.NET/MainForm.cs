using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;

namespace Esp32MjpegMotion
{
    public class MainForm : Form
    {
        private const string BaseHost = "http://192.168.4.1";
        private static readonly string PinUrl = $"{BaseHost}:8080/pin";
        private static readonly string StreamUrl = $"{BaseHost}:81/stream";
        private const string AuthToken = "SEJF_SHARED_TOKEN_123";

        private readonly string SaveDir = @"G:\Mój dysk\WykrycieRuchu";

        private readonly double MinMotionArea = 2500;
        private readonly int DiffThreshold = 10;
        private readonly int CooldownMs = 250;

        private readonly PictureBox picture = new PictureBox();
        private readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        private readonly ListBox logBox = new ListBox();
        private readonly Label pinLabel = new Label();
        private readonly Button btnStart = new Button();
        private readonly Button btnStop = new Button();
        private readonly Button btnSave = new Button();
        private readonly CheckBox chkPreview = new CheckBox();

        private CancellationTokenSource? streamCts;
        private CancellationTokenSource? pinCts;

        private volatile bool previewEnabled = true;

        private Mat? prevGray;
        private long lastSaveTicks = 0;

        private Bitmap? lastBitmap;
        private readonly object bitmapLock = new object();

        private static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        public MainForm()
        {
            Text = "Smart Safe - Projekt MARM";
            Width = 1100;
            Height = 780;
            MinimumSize = new System.Drawing.Size(900, 650);
            StartPosition = FormStartPosition.CenterScreen;

            Directory.CreateDirectory(SaveDir);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var top = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            root.Controls.Add(top, 0, 0);
            root.SetColumnSpan(top, 2);

            picture.Dock = DockStyle.Fill;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.BackColor = Color.Black;
            root.Controls.Add(picture, 0, 1);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            root.Controls.Add(right, 1, 1);

            btnStart.Text = "Start";
            btnStart.Width = 90;
            btnStart.Height = 34;
            btnStart.Left = 10;
            btnStart.Top = 18;

            btnStop.Text = "Stop";
            btnStop.Width = 90;
            btnStop.Height = 34;
            btnStop.Left = 110;
            btnStop.Top = 18;

            chkPreview.Text = "Podgląd";
            chkPreview.Left = 470;
            chkPreview.Top = 24;
            chkPreview.Width = 90;
            chkPreview.Checked = true;

            pinLabel.Width = 280;
            pinLabel.Height = 56;
            pinLabel.TextAlign = ContentAlignment.MiddleCenter;
            pinLabel.Text = "PIN: ----";
            pinLabel.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            pinLabel.BackColor = Color.Black;
            pinLabel.ForeColor = Color.Lime;
            pinLabel.BorderStyle = BorderStyle.FixedSingle;
            pinLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pinLabel.Top = 12;

            top.Resize += (_, __) => pinLabel.Left = top.Width - pinLabel.Width - 10;
            pinLabel.Left = top.Width - pinLabel.Width - 10;

            btnStart.Click += (_, __) => StartAll();
            btnStop.Click += (_, __) => StopAll();
            btnSave.Click += (_, __) => SaveManual();

            chkPreview.CheckedChanged += (_, __) =>
            {
                previewEnabled = chkPreview.Checked;
                picture.Visible = previewEnabled;

                if (previewEnabled)
                {
                    AddLog("Podgląd ON");
                    StartStream();
                }
                else
                {
                    AddLog("Podgląd OFF");
                    StopStream();
                    SetStatus("Podgląd OFF (PIN działa)");
                }
            };

            top.Controls.Add(btnStart);
            top.Controls.Add(btnStop);
            top.Controls.Add(chkPreview);
            top.Controls.Add(pinLabel);

            var lblLog = new Label
            {
                Text = "Log",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            right.Controls.Add(lblLog);

            logBox.Dock = DockStyle.Fill;
            right.Controls.Add(logBox);

            var statusStrip = new StatusStrip();
            statusLabel.Text = "Gotowe";
            statusStrip.Items.Add(statusLabel);
            Controls.Add(statusStrip);

            FormClosing += (_, __) => StopAll();

            StartPin();
        }
        private void StartAll()
        {
            StartPin();
            if (previewEnabled) StartStream();
            else SetStatus("Podgląd OFF (PIN działa)");
        }

        private void StopAll()
        {
            StopStream();
            StopPin();

            prevGray?.Dispose();
            prevGray = null;

            lock (bitmapLock)
            {
                lastBitmap?.Dispose();
                lastBitmap = null;
            }

            InvokeUI(() =>
            {
                var old = picture.Image;
                picture.Image = null;
                old?.Dispose();
            });

            SetStatus("Zatrzymano");
            AddLog("Zatrzymano wszystko");
        }

        private void StartPin()
        {
            if (pinCts != null) return;
            pinCts = new CancellationTokenSource();
            Task.Run(() => PinWorker(pinCts.Token));
            AddLog($"PIN worker start: {PinUrl}");
        }

        private void StopPin()
        {
            pinCts?.Cancel();
            pinCts = null;
            AddLog("PIN worker stop");
        }

        private void StartStream()
        {
            if (streamCts != null) return;

            streamCts = new CancellationTokenSource();

            prevGray?.Dispose();
            prevGray = null;

            SetStatus("Łączenie (stream)...");
            AddLog($"Stream start: {StreamUrl}");
            Task.Run(() => StreamLoopAsync(streamCts.Token));
        }

        private void StopStream()
        {
            streamCts?.Cancel();
            streamCts = null;

            prevGray?.Dispose();
            prevGray = null;

            InvokeUI(() =>
            {
                var old = picture.Image;
                picture.Image = null;
                old?.Dispose();
            });

            AddLog("Stream stop");
        }
        private async Task StreamLoopAsync(CancellationToken token)
        {
            int attempt = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ReadMjpegAsync(StreamUrl, token).ConfigureAwait(false);

                    attempt = 0;
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    attempt++;
                    var delay = Math.Min(8000, 400 * attempt);
                    SetStatus($"Stream error: {ex.Message} (retry {delay}ms)");
                    AddLog($"Stream error: {ex}");
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }
        }
        private async Task ReadMjpegAsync(string url, CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var ct = resp.Content.Headers.ContentType?.ToString() ?? "";
            if (!ct.Contains("multipart", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("To nie jest MJPEG multipart. Sprawdź URL/firmware.");

            string boundary = ExtractBoundary(ct);
            if (string.IsNullOrWhiteSpace(boundary))
                throw new InvalidOperationException("Brak boundary w Content-Type.");

            using var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);

            SetStatus("Połączono - odbieranie strumienia...");
            var reader = new MjpegReader(stream, boundary);

            while (!token.IsCancellationRequested)
            {
                var frame = await reader.ReadFrameAsync(token).ConfigureAwait(false);
                if (frame == null || frame.Length == 0) continue;

                HandleJpeg(frame);
            }
        }
        private async Task PinWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var pin = await FetchPinAsync(token).ConfigureAwait(false);
                    InvokeUI(() => pinLabel.Text = string.IsNullOrWhiteSpace(pin) ? "PIN: ----" : $"PIN: {pin}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    InvokeUI(() => pinLabel.Text = "PIN: ERR");
                }

                try { await Task.Delay(1000, token).ConfigureAwait(false); } catch { break; }
            }
        }
        private async Task<string?> FetchPinAsync(CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, PinUrl);
            req.Headers.TryAddWithoutValidation("X-Auth", AuthToken);

            using var resp = await http.SendAsync(req, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("pin", out var pinProp)) return null;
            var pin = pinProp.GetString();
            return string.IsNullOrWhiteSpace(pin) ? null : pin;
        }
        private static string ExtractBoundary(string contentType)
        {
            var parts = contentType.Split(';');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(9).Trim('"', ' ');
            }
            return "";
        }
        private sealed class MjpegReader
        {
            private readonly Stream _stream;
            private readonly byte[] _boundary;
            private readonly byte[] _buf = new byte[32 * 1024];
            private int _bufLen = 0;
            private int _bufPos = 0;
            public MjpegReader(Stream stream, string boundary)
            {
                _stream = stream;
                _boundary = Encoding.ASCII.GetBytes("--" + boundary);
            }
            public async Task<byte[]?> ReadFrameAsync(CancellationToken token)
            {
                await SkipToBoundaryAsync(token).ConfigureAwait(false);
                int contentLen = -1;
                while (true)
                {
                    var line = await ReadLineAsync(token).ConfigureAwait(false);
                    if (line.Length == 0) break;
                    var idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim();
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(val, out var len))
                    {
                        contentLen = len;
                    }
                }
                if (contentLen <= 0) return null;
                return await ReadExactlyAsync(contentLen, token).ConfigureAwait(false);
            }
            private async Task SkipToBoundaryAsync(CancellationToken token)
            {
                int match = 0;
                while (true)
                {
                    int b = await ReadByteAsync(token).ConfigureAwait(false);
                    if (b < 0) throw new EndOfStreamException();
                    if (b == _boundary[match])
                    {
                        match++;
                        if (match == _boundary.Length)
                        {
                            while (true)
                            {
                                int x = await ReadByteAsync(token).ConfigureAwait(false);
                                if (x < 0) throw new EndOfStreamException();
                                if (x == '\n') return;
                            }
                        }
                    }
                    else match = 0;
                }
            }
            private async Task<string> ReadLineAsync(CancellationToken token)
            {
                using var ms = new MemoryStream();
                while (true)
                {
                    int b = await ReadByteAsync(token).ConfigureAwait(false);
                    if (b < 0) throw new EndOfStreamException();
                    if (b == '\n') break;
                    ms.WriteByte((byte)b);
                }
                return Encoding.ASCII.GetString(ms.ToArray()).TrimEnd('\r');
            }
            private async Task<byte[]> ReadExactlyAsync(int length, CancellationToken token)
            {
                var outBuf = new byte[length];
                int written = 0;
                while (written < length)
                {
                    int available = _bufLen - _bufPos;
                    if (available <= 0)
                    {
                        _bufPos = 0;
                        _bufLen = await _stream.ReadAsync(_buf, 0, _buf.Length, token).ConfigureAwait(false);
                        if (_bufLen == 0) throw new EndOfStreamException();
                        available = _bufLen;
                    }
                    int take = Math.Min(available, length - written);
                    Buffer.BlockCopy(_buf, _bufPos, outBuf, written, take);
                    _bufPos += take;
                    written += take;
                }
                return outBuf;
            }
            private async Task<int> ReadByteAsync(CancellationToken token)
            {
                if (_bufPos >= _bufLen)
                {
                    _bufPos = 0;
                    _bufLen = await _stream.ReadAsync(_buf, 0, _buf.Length, token).ConfigureAwait(false);
                    if (_bufLen == 0) return -1;
                }
                return _buf[_bufPos++];
            }
        }
        private void HandleJpeg(byte[] jpg)
        {
            if (!previewEnabled) return;
            Bitmap? uiBmp = null;
            Bitmap? saveBmp = null;
            try
            {
                using var ms = new MemoryStream(jpg);
                uiBmp = new Bitmap(ms);
                saveBmp = new Bitmap(uiBmp);
            }
            catch
            {
                uiBmp?.Dispose();
                saveBmp?.Dispose();
                return;
            }
            lock (bitmapLock)
            {
                lastBitmap?.Dispose();
                lastBitmap = saveBmp;
            }
            bool motion = false;
            try
            {
                using Mat frameRaw = BitmapToMatBgr(uiBmp);
                using Mat frame = new Mat();
                Cv2.Rotate(frameRaw, frame, RotateFlags.Rotate90Clockwise);
                using Mat gray = new Mat();
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(7, 7), 0);
                if (prevGray != null && !prevGray.Empty())
                {
                    using Mat diff = new Mat();
                    Cv2.Absdiff(prevGray, gray, diff);
                    using Mat thresh = new Mat();
                    Cv2.Threshold(diff, thresh, DiffThreshold, 255, ThresholdTypes.Binary);
                    using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
                    Cv2.Dilate(thresh, thresh, kernel, iterations: 2);
                    Cv2.FindContours(thresh, out OpenCvSharp.Point[][] contours, out _,
                        RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var contour in contours)
                    {
                        double area = Cv2.ContourArea(contour);
                        if (area > MinMotionArea)
                        {
                            motion = true;
                            var rect = Cv2.BoundingRect(contour);
                            Cv2.Rectangle(frame, rect, Scalar.Red, 2);
                        }
                    }
                    Cv2.AddWeighted(prevGray, 0.9, gray, 0.1, 0, prevGray);
                }
                else
                {
                    prevGray?.Dispose();
                    prevGray = gray.Clone();
                }
                uiBmp.Dispose();
                uiBmp = MatToBitmap(frame);
            }
            catch (Exception ex)
            {
                SetStatus($"Błąd OpenCV: {ex.Message}");
                motion = false;
            }
            InvokeUI(() =>
            {
                var oldImg = picture.Image;
                picture.Image = uiBmp;
                oldImg?.Dispose();
            });
            SetStatus(motion ? "RUCH WYKRYTY!" : "Monitorowanie");
            if (motion)
            {
                long now = Environment.TickCount64;
                if (now - lastSaveTicks > CooldownMs)
                {
                    lastSaveTicks = now;
                    SaveMotionFrame();
                }
            }
        }
        private void SaveMotionFrame()
        {
            Bitmap? bmpCopy = null;
            lock (bitmapLock)
            {
                if (lastBitmap != null) bmpCopy = (Bitmap)lastBitmap.Clone();
            }
            if (bmpCopy == null) return;
            try
            {
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var path = Path.Combine(SaveDir, $"motion_{ts}.jpg");
                bmpCopy.Save(path, ImageFormat.Jpeg);
                SetStatus($"RUCH! Zapisano: {Path.GetFileName(path)}");
                AddLog($"Saved: {path}");
            }
            catch (Exception ex)
            {
                SetStatus($"Błąd zapisu: {ex.Message}");
                AddLog($"Save error: {ex}");
            }
            finally
            {
                bmpCopy.Dispose();
            }
        }
        private void SaveManual()
        {
            Bitmap? bmpCopy = null;
            lock (bitmapLock)
            {
                if (lastBitmap != null) bmpCopy = (Bitmap)lastBitmap.Clone();
            }
            if (bmpCopy == null)
            {
                SetStatus("Brak obrazu do zapisania");
                return;
            }
            try
            {
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = Path.Combine(SaveDir, $"manual_{ts}.jpg");
                bmpCopy.Save(path, ImageFormat.Jpeg);
                SetStatus($"Zapisano: {Path.GetFileName(path)}");
                AddLog($"Manual saved: {path}");
            }
            catch (Exception ex)
            {
                SetStatus($"Błąd zapisu: {ex.Message}");
                AddLog($"Manual save error: {ex}");
            }
            finally
            {
                bmpCopy.Dispose();
            }
        }
        private static Mat BitmapToMatBgr(Bitmap bitmap)
        {
            var bmp = bitmap.PixelFormat == PixelFormat.Format24bppRgb
                ? bitmap
                : bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format24bppRgb);
            try
            {
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                try
                {
                    var mat = Mat.FromPixelData(bmp.Height, bmp.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
                    return mat.Clone();
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
            finally
            {
                if (!ReferenceEquals(bmp, bitmap)) bmp.Dispose();
            }
        }
        private static Bitmap MatToBitmap(Mat matBgr)
        {
            using var rgb = new Mat();
            Cv2.CvtColor(matBgr, rgb, ColorConversionCodes.BGR2RGB);
            using var ms = new MemoryStream();
            Cv2.ImEncode(".bmp", rgb, out var bytes);
            ms.Write(bytes, 0, bytes.Length);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        private void SetStatus(string text) => InvokeUI(() => statusLabel.Text = text);
        private void AddLog(string text)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {text}";
            InvokeUI(() =>
            {
                logBox.Items.Insert(0, line);
                if (logBox.Items.Count > 300) logBox.Items.RemoveAt(logBox.Items.Count - 1);
            });
        }
        private void InvokeUI(Action action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }
    }
}