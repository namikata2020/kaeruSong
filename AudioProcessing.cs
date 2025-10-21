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

        // Hann window
        private static double[] Hann(int length)
        {
            var w = new double[length];
            for (int i = 0; i < length; i++)
            {
                w[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (length - 1));
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

        public static int GetPeriod(double[] waveData, int periodMin, int periodMax, int corrSize)
        {
            double corrMax = 0.0;
            int period = periodMin;

            for (int p = periodMin; p < periodMax; p++)
            {
                double corr = CalcAutocorr(waveData, corrSize, p);
                if (corr > corrMax)
                {
                    corrMax = corr;
                    period = p;
                }
            }
            return period;
        }

        public static double[] TimeStretch(double[] dataIn, int fs, double rate)
        {
            int nSamples = dataIn.Length;
            int corrSize = (int)(fs * 0.01);   // 10ms
            int minPeriod = (int)(fs * 0.005); // 5ms
            int maxPeriod = (int)(fs * 0.02);  // 20ms

            int offsetIn = 0;
            int offsetOut = 0;

            var dataOut = new double[(int)(nSamples / rate) + 1];

            while (offsetIn + maxPeriod * 2 < nSamples)
            {
                int period = GetPeriod(dataIn.Skip(offsetIn).ToArray(), minPeriod, maxPeriod, corrSize);

                if (rate >= 1.0) // fast
                {
                    var window = Hann(2 * period);
                    for (int n = 0; n < period; n++)
                    {
                        dataOut[offsetOut + n] =
                            dataIn[offsetIn + n] * window[period + n] +
                            dataIn[offsetIn + period + n] * window[n];
                    }

                    int q = (int)(period / (rate - 1.0) + 0.5);
                    for (int n = period; n < nSamples; n++)
                    {
                        if (n >= q) break;
                        if (offsetIn + period + n >= nSamples) break;
                        dataOut[offsetOut + n] = dataIn[offsetIn + period + n];
                    }

                    offsetIn += period + q;
                    offsetOut += q;
                }
                else // slow
                {
                    Array.Copy(dataIn, offsetIn, dataOut, offsetOut, period);

                    var window = Hann(2 * period);
                    for (int n = 0; n < period; n++)
                    {
                        dataOut[offsetOut + period + n] =
                            dataIn[offsetIn + n] * window[n] +
                            dataIn[offsetIn + period + n] * window[period + n];
                    }

                    int q = (int)(period * rate / (1.0 - rate) + 0.5);
                    for (int n = period; n < nSamples; n++)
                    {
                        if (n >= q) break;
                        if (offsetIn + period + n >= nSamples) break;
                        dataOut[offsetOut + period + n] = dataIn[offsetIn + n];
                    }

                    offsetIn += q;
                    offsetOut += period + q;
                }
            }

            return dataOut;
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