using System;
using System.Linq;

namespace kaeruSong
{
    public static class AudioProcessing
    {
        // sinc(x) = sin(pi*x)/(pi*x)
        private static double Sinc(double x)
        {
            if (Math.Abs(x) < 1e-8) return 1.0;
            return Math.Sin(Math.PI * x) / (Math.PI * x);
        }

        // ハン窓（0..N-1）
        private static double[] Hann(int N)
        {
            var w = new double[N];
            // 端点を含む標準ハン窓
            for (int n = 0; n < N; n++)
            {
                w[n] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / (N - 1));
            }
            return w;
        }

        public static double[] Resampling(double[] dataIn, double pitch, int nTerm)
        {
            int nSamples = dataIn.Length;
            int nSamplesOut = (int)(nSamples / pitch);
            var dataOut = new double[nSamplesOut];

            for (int n = 0; n < nSamplesOut; n++)
            {
                double analogTime = pitch * n;
                int digitalTime = (int)analogTime;

                int start = digitalTime - nTerm / 2;
                int end = digitalTime + nTerm / 2;

                double sum = 0.0;
                for (int k = start; k <= end; k++)
                {
                    if (k >= 0 && k < nSamples)
                    {
                        double sincVal = Sinc(analogTime - k);
                        sum += dataIn[k] * sincVal;
                    }
                }
                dataOut[n] = sum;
            }
            return dataOut;
        }

        public static double CalcAutocorr(double[] waveData, int corrSize, int lag)
        {
            double autocorr = 0.0;
            for (int i = 0; i < corrSize; i++)
            {
                autocorr += waveData[i] * waveData[i + lag];
            }
            return autocorr;
        }

        // 自己相関に基づく周期推定（配列コピーなし・オフセット指定）
        private static int GetPeriod(double[] x, int inPos, int minP, int maxP, int corrSize)
        {
            int nSamples = x.Length;

            // 参照窓の有効長（末尾を越えないように）
            int refLen = Math.Min(corrSize, nSamples - inPos - maxP);
            if (refLen <= 0) return minP;

            // 参照窓エネルギー
            double E0 = 0.0;
            for (int n = 0; n < refLen; n++)
                E0 += x[inPos + n] * x[inPos + n];

            double best = double.NegativeInfinity;
            int bestLag = minP;

            for (int p = minP; p <= maxP; p++)
            {
                double num = 0.0, E1 = 0.0;
                int baseB = inPos + p;

                for (int n = 0; n < refLen; n++)
                {
                    double a = x[inPos + n];
                    double b = x[baseB + n];
                    num += a * b;
                    E1 += b * b;
                }

                double denom = Math.Sqrt(E0 * E1) + 1e-12;
                double r = num / denom; // 正規化相関

                if (r > best)
                {
                    best = r;
                    bestLag = p;
                }
            }

            return bestLag;
        }

        public static double[] TimeStretch(double[] x, int fs, double rate)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);

            int corrSize = Math.Max(1, (int)Math.Round(fs * 0.01)); // 30ms
            int minPeriod = Math.Max(1, (int)Math.Round(fs * 0.005)); // 5ms
            int maxPeriod = Math.Max(minPeriod + 1, (int)Math.Round(fs * 0.02)); // 20ms以上
            if (x.Length == 0) return Array.Empty<double>();
            if (Math.Abs(rate - 1.0) < 1e-12) // レート=1はそのまま返す
                return (double[])x.Clone();

            Array.Resize(ref x, x.Length + 2 * maxPeriod); // 最後目で変換されるように配列の最後に0を追加する
            int nSamples = x.Length;

            // 出力長は「安全側」へ。最後にトリムする。
            int maxOutLen = (int)Math.Ceiling(nSamples / Math.Max(rate, 1e-6)) + maxPeriod * 2 + 8;
            var y = new double[maxOutLen];

            int inPos = 0;
            int outPos = 0;

            while (inPos + 2 * maxPeriod < nSamples && outPos + 2 * maxPeriod < y.Length)
            {
                int period = GetPeriod(x, inPos, minPeriod, maxPeriod, corrSize);

                int L = period;
                var w = Hann(2 * L);

                if (rate >= 1.0)
                {
                    // 2フレームをハン窓で重ねて L サンプル分出力
                    for (int n = 0; n < L; n++)
                    {
                        int inA = inPos + n;
                        int inB = inPos + L + n;
                        int outIdx = outPos + n;

                        if (inA >= nSamples || inB >= nSamples || outIdx >= y.Length) break;

                        y[outIdx] = x[inA] * w[L + n] + x[inB] * w[n];
                    }

                    // rate>1: スキップ量 q を計算（間引き）
                    int q = (int)Math.Round(L / (rate - 1.0));
                    // 次の L の後ろに q サンプルをコピー（間を詰める）
                    for (int n = 0; n < q; n++)
                    {
                        int inIdx = inPos + L + n;
                        int outIdx = outPos + L + n;
                        if (inIdx >= nSamples || outIdx >= y.Length) break;

                        y[outIdx] = x[inIdx];
                    }

                    inPos += L + q;
                    //outPos += L + q;
                    outPos += q;
                }
                else
                {
                    // rate<1: まず L サンプルそのまま出力（伸長の基礎）
                    for (int n = 0; n < L; n++)
                    {
                        int inIdx = inPos + n;
                        int outIdx = outPos + n;
                        if (inIdx >= nSamples || outIdx >= y.Length) break;

                        y[outIdx] = x[inIdx];
                    }

                    // 次に 2 つのフレームをハン窓で重ねて L サンプル追記（重複生成）
                    for (int n = 0; n < L; n++)
                    {
                        int inA = inPos + n;
                        int inB = inPos + L + n;
                        int outIdx = outPos + L + n;

                        if (inA >= nSamples || inB >= nSamples || outIdx >= y.Length) break;

                        y[outIdx] = x[inA] * w[n] + x[inB] * w[L + n];
                    }

                    // 追加の繰り返し量 q を計算（伸ばす）
                    //int q = (int)(period * rate / (1.0 - rate) + 0.5);
                    int q = (int)Math.Round(L * rate / (1.0 - rate));
                    //int q = (int)Math.Round(L  / rate) - 1;
                    for (int n = 0; n < q; n++)
                    {
                        int inIdx = inPos + n;
                        int outIdx = outPos + 2 * L + n;
                        if (inIdx >= nSamples || outIdx >= y.Length) break;

                        y[outIdx] = x[inIdx];
                    }

                    inPos += q;
                    //outPos += 2 * L + q;
                    outPos += L + q;
                }
            }

            // 実長へトリム
            if (outPos <= 0) return Array.Empty<double>();
            Array.Resize(ref y, outPos);
            return y;
        }

        public static double[] PitchShift(double[] signal, int sampleRate, double pitch, int nTerm)
        {
            double rate = 1.0 / pitch;
            var resampled = Resampling(signal, pitch, nTerm);
            var stretched = TimeStretch(resampled, sampleRate, rate);

            return stretched;
        }
    }
}
