/*
 * オプションパッケージ
 * MathNet.Numerics
 * NAudio
 * OxyPlot
 * 
 */
using NAudio.Wave;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace kaeruSong
{
    public partial class Form1 : Form
    {
        WaveIn waveIn;                                  // NAudio録音用オブジェクト
        WaveOutEvent waveOut;                           // NAudio再生用オブジェクト
        bool isRecording = false;                       // 録音中フラグ
        bool isPlaying = false;                         // 再生中フラグ
        List<double> rawSoundData = new List<double>(); // 音声したすべてのデータ
        double[] soundValues;                           // 選択された音声データ
        double pitchValue;                              // ピッチ周波数
        private int sampleIndex = 0;                    //グラフ描画のためのX軸計算用
        private int samplingFrequency = 48000;          //サンプリング周波数
        int SmouseX, SmouseY, EmouseX, EmouseY;         // マウスのドラッグ開始位置と終了位置
        bool isMouseDown = false;                       // マウスの左ボタンが押されているかどうか
        private LineSeries plotDataSeries = new LineSeries();　// グラフ描画用座標データ
        public Form1()
        {
            InitializeComponent();
            InitPlot();
            Text = "Kaeru Sound";                   // ウィンドウのテキストを設定
            Load += Form1_Load;                     // フォームがロードされたときのイベントハンドラを設定
            button1.Text = "start";                 // button1のテキストを設定
            button2.Text = "stop";                  // button2のテキストを設定
            button3.Text = "play";                  // button3のテキストを設定
            button4.Text = "raw data";              // button4のテキストを設定
            button5.Text = "music";                 // button5のテキストを設定
            button1.Click += start_Click;           // button1がクリックされたときのイベントハンドラを設定
            button2.Click += stop_Click;            // button2がクリックされたときのイベントハンドラを設定
            button3.Click += play_Click;            // button3がクリックされたときのイベントハンドラを設定
            button4.Click += reset_Click;           // button4がクリックされたときのイベントハンドラを設定
            button5.Click += music_Click;           // button5がクリックされたときのイベントハンドラを設定

            plotView1.Controller = new PlotController();
            plotView1.Controller.UnbindMouseDown(OxyMouseButton.Left); // 左クリックを無効化
            plotView1.MouseDown += Chart1_MouseDown;   // マウスのボタンが押されたとき
            plotView1.MouseMove += Chart1_MouseMove;   // マウスが移動したとき
            plotView1.MouseUp += Chart1_MouseUp;       // マウスのボタンが離されたとき
            plotView1.Paint += DrawFigures;            // chart1のウインドウが再描画されるとき

            label1.Text = "Recording Device";
            label2.Text = "MPM type 2";
            label3.Text = "Yin Type 2";
            label4.Text = "Audio Waveform";
            label5.Text = "Pitch Estimation";
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++) // 録音デバイスの数だけ繰り返す
            {
                var cap = WaveIn.GetCapabilities(i);
                comboBox1.Items.Add(cap.ProductName);
                comboBox1.SelectedIndex = 0;
            }
        }
        private void Chart1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {

                // クライアント領域のサイズを取得 clientSize.Width と clientSize.Height
                Size clientSize = plotView1.ClientSize;

                // マウスの座標がクライアント領域の外に出ないようにする
                EmouseX = Math.Max(0, Math.Min(e.X, clientSize.Width - 1));
                EmouseY = Math.Max(0, Math.Min(e.Y, clientSize.Height - 1));
                isMouseDown = false;

                // ドラッグ量を計算 グラフの座標に変換　SmouseX,Y, EmouseX,Yはグラフィック座標
                var xAxis = plotView1.ActualModel.Axes.First(a => a.Position == AxisPosition.Bottom);
                var yAxis = plotView1.ActualModel.Axes.First(a => a.Position == AxisPosition.Left);
                double nowX = xAxis.InverseTransform(SmouseX);
                double oldX = xAxis.InverseTransform(EmouseX);
                double nowY = yAxis.InverseTransform(SmouseY);
                double oldY = yAxis.InverseTransform(EmouseY);

                // 数値の小さいほうがstartIndex, 大きいほうがendIndexになるようにMath関数で書き換え
                int startIndex = (int)(Math.Min(nowX, oldX) * samplingFrequency);
                int endIndex = (int)(Math.Max(nowX, oldX) * samplingFrequency);

                // 取り出すデータはPoints形式
                var selectedPoints = plotDataSeries.Points.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
                var tmpValues = selectedPoints.Select(p => p.Y).ToArray();
                // -1.0～+1.0に正規化
                double min = tmpValues.Min();
                double max = tmpValues.Max();
                soundValues = tmpValues.Select(x => NormalizeToMinusOneToOne(x, min, max)).ToArray();
                // グラフに新しいデータを追加する
                plotDataSeries.Points.Clear();      // グラフのクリア
                plotView1.InvalidatePlot(true);
                for (int i = 0; i < soundValues.Length; i++)
                {
                    double plotX = (double)i / (double)samplingFrequency;
                    plotDataSeries.Points.Add(new DataPoint(plotX, soundValues[i]));
                }
                EmouseX = SmouseX = 0;
                EmouseY = SmouseY = 0;
                plotView1.Invalidate(); // chart1を再描画してペイントイベントを発生させる
                pitchEstimation();
            }
        }

        // 値を[-1, 1]の範囲に正規化する関数
        static double NormalizeToMinusOneToOne(double value, double min, double max)
        {
            // 正規化: [min, max] → [-1, 1]
            return 2 * ((value - min) / (max - min)) - 1;
        }
        private void Chart1_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown == true)
            {
                // クライアント領域のサイズを取得 clientSize.Width clientSize.Height
                // マウスの座標がクライアント領域の外に出ないようにする
                Size clientSize = plotView1.ClientSize;

                EmouseX = Math.Max(0, Math.Min(e.X, clientSize.Width - 1));
                EmouseY = Math.Max(0, Math.Min(e.Y, clientSize.Height - 1));
                plotView1.Invalidate(); // グラフを再描画してペイントイベントを発生させる

            }
        }
        private void Chart1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // マウスの位置を記録
                SmouseX = e.X;
                SmouseY = e.Y;

                isMouseDown = true;
            }
        }

        // 範囲を選択するための四角形を描画
        private void DrawFigures(object sender, PaintEventArgs e)
        {
            // アンチエイリアスを有効にする
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // 半透明のブラシを作成 (アルファ値128, 色(R,G,B))
            SolidBrush brush = new SolidBrush(Color.FromArgb(128, 120, 120, 180));
            // クライアント領域のサイズを取得 clientSize.Width clientSize.Height
            Size clientSize = plotView1.ClientSize;
            // 四角形を描画する
            int width = EmouseX - SmouseX;
            int height = EmouseY - SmouseY;

            e.Graphics.FillRectangle(brush, SmouseX, 0, width, clientSize.Height); // 四角形形
        }

        // 録音データがあるときに呼び出されるイベントハンドラ
        private void WaveInDataAvailable(object sender, WaveInEventArgs e)
        {
            ///float型にしつつ最大-1.0f～+1.0fに制限する
            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, index); ;
                double sample32 = sample / 32768f;
                double plotX = (double)sampleIndex / (double)samplingFrequency;
                plotDataSeries.Points.Add(new DataPoint(plotX, sample32));
                plotView1.InvalidatePlot(true);
                sampleIndex++;
                rawSoundData.Add(sample32);
            }
        }

        // グラフの初期化
        void InitPlot()
        {
            var model = new PlotModel();

            model.Axes.Add(new LinearAxis { Minimum = -1, Maximum = 1, Position = AxisPosition.Left, });
            model.Series.Add(plotDataSeries);
            plotView1.Model = model;
        }

        // 録音の開始
        private void start_Click(object sender, EventArgs e)
        {
            if(WaveIn.DeviceCount < 1)
            {
                MessageBox.Show("録音デバイスが見つかりません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!isRecording && !isPlaying)
            {
                plotDataSeries.Points.Clear();      // グラフのクリア
                plotView1.InvalidatePlot(true);
                rawSoundData.Clear();               // 録音データのクリア
                sampleIndex = 0;
                var deviceNumber = comboBox1.SelectedIndex;

                waveIn = new WaveIn()
                {
                    DeviceNumber = deviceNumber, // Default
                };
                waveIn.DataAvailable += WaveInDataAvailable;
                waveIn.RecordingStopped += (_, __) =>
                {
                    soundValues = rawSoundData.ToArray();
                    pitchEstimation();
                };
                waveIn.WaveFormat = new WaveFormat(sampleRate: samplingFrequency, channels: 1);
                ///sampleRateは音声認識用の波形に必要なherz数 * 1000

                waveIn.StartRecording();
                isRecording = true;
            }
        }

        // 録音と再生の停止
        private void stop_Click(object sender, EventArgs e)
        {
            if (isRecording)
            {
                waveIn?.StopRecording();
                waveIn?.Dispose();
                isRecording = false;
            }
            if (isPlaying)
            {
                waveOut?.Stop();
                waveOut?.Dispose();
                isPlaying = false;
            }
        }

        // 選択した範囲の音声を再生
        private void play_Click(object sender, EventArgs e)
        {
            if (soundValues == null) return;
            if (!isRecording && !isPlaying)
            {
                // double[] → byte[] に変換（16bit PCM）
                byte[] pcmData = ConvertToPCM(soundValues);
                // NAudioで再生
                var waveFormat = new WaveFormat(samplingFrequency, 16, 1); // 16bit, モノラル
                var pcmStream = new RawSourceWaveStream(new MemoryStream(pcmData), waveFormat);
                // ストリーミング用　再生が終了しない
                // var buffer = new BufferedWaveProvider(waveFormat);
                // buffer.AddSamples(pcmData, 0, pcmData.Length);

                waveOut = new WaveOutEvent();


                // 再生終了時のコールバックを設定
                waveOut.PlaybackStopped += (__, ee) =>
                {
                    isPlaying = false;
                    waveOut?.Dispose();
                    // ボタン2にフォーカスを移動
                    button2.Focus();
                };
                // メモリ上の音声データの再生
                waveOut.Init(pcmStream);
                waveOut.Play();
                isPlaying = true;
            }
        }

        // かえるのうたをピッチシフトして再生
        private void music_Click(object sender, EventArgs e)
        {
            if (soundValues == null) return;
            List<double> kaeruout = new List<double>();
            double[] kaeru = [ 261.62, 293.66, 329.62, 349.22, 329.62, 293.66, 261.62, 329.62, 349.22, 391.99, 440, 349.22, 329.62, 261.62, 0, 261.62, 0, 261.62, 0, 261.62, 0, 261.62, 261.62, 293.66, 293.66, 329.62, 329.62, 349.22, 329.62, 293.66, 261.62 ];
            double[] kaerulen = [4, 4, 4, 4, 4, 4, 2, 4, 4, 4, 4, 4, 2, 4, 4, 4, 4, 4, 4, 4, 4, 8, 8, 8, 8, 8, 8, 8, 4, 4, 4];

            int n_term = 30;                  // 標本化定理の近似項数
            if (!isRecording && !isPlaying)
            {
                for(int i=0;i<kaeru.Length; i++)
                {
                    if(kaeru[i] == 0)
                    {
                        int restlen = (int)(samplingFrequency / kaerulen[i]) * 2;
                        for(int j=0; j< restlen; j++)
                        {
                            kaeruout.Add(0.0);
                        }
                    }
                    else
                    {
                        var pitch = (kaeru[i]) / pitchValue; // ピッチシフトの倍率
                        int length = (int)(samplingFrequency / kaerulen[i]) * 2;
                        if(length > soundValues.Length) length = soundValues.Length;
                        double[] tone = soundValues[0..length];
                        double[] convpitch = AudioProcessing.PitchShift(tone, samplingFrequency, pitch, n_term);
                        kaeruout.AddRange(convpitch);
                    }
                }

                // double[] → byte[] に変換（16bit PCM）
                var kaeruValues = kaeruout.ToArray();
                byte[] pcmData = ConvertToPCM(kaeruValues);
                // NAudioで再生
                var waveFormat = new WaveFormat(samplingFrequency, 16, 1); // 16bit, モノラル
                var pcmStream = new RawSourceWaveStream(new MemoryStream(pcmData), waveFormat);

                waveOut = new WaveOutEvent();


                // 再生終了時のコールバックを設定
                waveOut.PlaybackStopped += (__, ee) =>
                {
                    isPlaying = false;
                    waveOut?.Dispose();
                    // ボタン2にフォーカスを移動
                    button2.Focus();
                };
                // メモリ上の音声データの再生
                waveOut.Init(pcmStream);
                waveOut.Play();
                isPlaying = true;
            }
        }

        // double[] → byte[] に変換（16bit PCM）
        static byte[] ConvertToPCM(double[] samples)
        {
            byte[] pcm = new byte[samples.Length * 2]; // 16bit = 2byte
            for (int i = 0; i < samples.Length; i++)
            {
                short val = (short)(Math.Max(-1.0, Math.Min(1.0, samples[i])) * short.MaxValue);
                pcm[i * 2] = (byte)(val & 0xFF);
                pcm[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
            }
            return pcm;
        }

        // 録音したデータの範囲選択を解除しすべてのデータを表示
        private void reset_Click(object sender, EventArgs e)
        {
            if (rawSoundData.Count != 0)
            {
                plotDataSeries.Points.Clear();      // グラフのクリア
                plotView1.InvalidatePlot(true);
                for (int i = 0; i < rawSoundData.Count; i++)
                {
                    double plotX = (double)i / (double)samplingFrequency;
                    plotDataSeries.Points.Add(new DataPoint(plotX, rawSoundData[i]));
                }
                plotView1.InvalidatePlot(true);
                soundValues = rawSoundData.ToArray();
                pitchEstimation();
            }
        }

        // ピッチの推定
        // soundValuesに入っている音声データを解析して、ピッチを推定する
        private void pitchEstimation()
        {
            if (soundValues != null)
            {
                var mpmtype1 = MPM.MPMType2(soundValues, samplingFrequency);
                var mpmtype2 = MPM.YinType2(soundValues, samplingFrequency);
                textBox1.Text = mpmtype1.ToString("F2") + " Hz (MPM Type2)";
                textBox2.Text = mpmtype2.ToString("F2") + " Hz (YinNsd Type2)";
                if (!double.IsNaN(mpmtype1))
                    pitchValue = mpmtype1;
                else
                    pitchValue = mpmtype2;

                // グラフ作成
                LineSeries pitchDataSeries1 = new LineSeries { Title = "mpm", Color = OxyColors.Blue }; // グラフ描画用座標データ
                LineSeries pitchDataSeries2 = new LineSeries { Title = "yin", Color = OxyColors.Red }; // グラフ描画用座標データ
                var model = new PlotModel();
                var legend = new Legend
                {
                    LegendPosition = LegendPosition.RightTop,
                    LegendPlacement = LegendPlacement.Inside,
                    LegendOrientation = LegendOrientation.Vertical,
                    LegendBorderThickness = 0
                };
                model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, });
                model.IsLegendVisible = true;
                model.Series.Add(pitchDataSeries1);
                model.Series.Add(pitchDataSeries2);
                plotView2.Model = model;
                plotView2.Model.Legends.Add(legend);
                var pitchtype1 = MPM.NormalizedSquareDifferenceType2(soundValues);
                int maxpoint = 1000;
                if (maxpoint > pitchtype1.Length) maxpoint = pitchtype1.Length;
                for (int i = 0; i < maxpoint; i++)
                {
                    double plotX = (double)i / (double)samplingFrequency;
                    pitchDataSeries1.Points.Add(new DataPoint(plotX, pitchtype1[i]));
                }
                var diff = MPM.DifferenceType2(soundValues);
                var pitchtype2 = MPM.CumulativeMeanNormalizedDifference(diff);
                for (int i = 0; i < maxpoint; i++)
                {
                    double plotX = (double)i / (double)samplingFrequency;
                    pitchDataSeries2.Points.Add(new DataPoint(plotX, pitchtype2[i]));
                }
            }
        }
    }
}
// test