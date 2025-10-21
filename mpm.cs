using System;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace kaeruSong
{
    public class MPM
    {
        const double MPM_K = 0.5;

        public static double[] FFT(double[] signal)
        {
            var complex = signal.Select(x => new System.Numerics.Complex(x, 0)).ToArray();
            Fourier.Forward(complex, FourierOptions.Matlab);
            return complex.Select(c => c.Real).ToArray();
        }

        public static double[] IFFT(System.Numerics.Complex[] spectrum)
        {
            Fourier.Inverse(spectrum, FourierOptions.Matlab);
            return spectrum.Select(c => c.Real).ToArray();
        }

        public static double[] AutocorrelationType1(double[] signal)
        {
            var complex = signal.Select(x => new System.Numerics.Complex(x, 0)).ToArray();
            Fourier.Forward(complex, FourierOptions.Matlab);
            var spec = complex;
            var conj = spec.Select(c => System.Numerics.Complex.Conjugate(c)).ToArray();
            var product = spec.Zip(conj, (a, b) => a * b).ToArray();
            return IFFT(product).Take(signal.Length).ToArray();
        }

        public static double[] AutocorrelationType2(double[] signal)
        {
            int len = signal.Length;
            var padded = signal.Concat(new double[len]).ToArray();
            var complex = padded.Select(x => new System.Numerics.Complex(x, 0)).ToArray();
            Fourier.Forward(complex, FourierOptions.Matlab);
            var spec = complex;
            var conj = spec.Select(c => System.Numerics.Complex.Conjugate(c)).ToArray();
            var product = spec.Zip(conj, (a, b) => a * b).ToArray();
            return IFFT(product).Take(len).ToArray();
        }

        public static double[] NormalizedSquareDifferenceType1(double[] signal)
        {
            var corr = AutocorrelationType1(signal);
            return corr[0] != 0 ? corr.Select(c => c / corr[0]).ToArray() : corr;
        }

        public static double[] NormalizedSquareDifferenceType2(double[] signal)
        {
            var corr = AutocorrelationType2(signal);
            var square = signal.Select(x => x * x).Reverse().ToArray();
            var cumsum = new double[square.Length];
            double sum = 0;
            for (int i = 0; i < square.Length; i++)
            {
                sum += square[i];
                cumsum[i] = sum;
            }
            Array.Reverse(cumsum);
            for (int i = 0; i < cumsum.Length; i++)
            {
                if (cumsum[i] < 1) cumsum[i] = 1;
            }
            return corr.Select((c, i) => c / (corr[0] + cumsum[i])).ToArray();
        }


        public static int? EstimatePeriod(double[] diff)
        {
            int start = 0;
            var mini = diff.Min();
            if (mini <= 0) mini = 0;
            while (start < diff.Length && diff[start] > mini) start++;
            if (start >= diff.Length) return null;

            double threshold = MPM_K * diff.Skip(start).Max();
            bool isNegative = true;
            int? maxIndex = null;

            for (int i = start; i < diff.Length; i++)
            {
                if (isNegative)
                {
                    if (diff[i] < 0) continue;
                    maxIndex = i;
                    isNegative = false;
                }
                if (diff[i] < 0)
                {
                    isNegative = true;
                    if (maxIndex.HasValue && diff[maxIndex.Value] >= threshold)
                        return maxIndex;
                }
                if (maxIndex.HasValue && diff[i] > diff[maxIndex.Value])
                    maxIndex = i;
            }
            return null;
            //return maxIndex;
        }

        public static double ParabolicInterpolation(double[] array, int x)
        {
            if (x < 1)
                return array[x] <= array[x + 1] ? x : x + 1;
            if (x >= array.Length - 1)
                return array[x] <= array[x - 1] ? x : x - 1;

            double denom = array[x + 1] + array[x - 1] - 2 * array[x];
            double delta = array[x - 1] - array[x + 1];
            if (denom == 0) return x;
            return x + delta / (2 * denom);
        }

        public static double MPMType1(double[] signal, int samplerate)
        {
            var nsd = NormalizedSquareDifferenceType1(signal);
            var index = EstimatePeriod(nsd);
            if(!index.HasValue) return double.NaN;
            var paraVal = ParabolicInterpolation(nsd, index.Value);
            if(paraVal == 0) return double.NaN;
            return samplerate / paraVal;
        }

        public static double MPMType2(double[] signal, int samplerate)
        {
            var nsd = NormalizedSquareDifferenceType2(signal);
            var index = EstimatePeriod(nsd);
            if (!index.HasValue) return double.NaN;
            var paraVal = ParabolicInterpolation(nsd, index.Value);
            if (paraVal == 0) return double.NaN;
            return samplerate / paraVal;
        }

        // difference_type1
        public static double[] DifferenceType1(double[] sig)
        {
            double[] autocorr = AutocorrelationType1(sig);
            double[] result = new double[autocorr.Length];

            for (int i = 0; i < autocorr.Length; i++)
            {
                result[i] = autocorr[0] - autocorr[i];
            }

            return result;
        }

        // difference_type2
        public static double[] DifferenceType2(double[] sig)
        {
            double[] autocorr = AutocorrelationType2(sig);

            // energy = (sig * sig)[::-1].cumsum()[::-1]
            int n = sig.Length;
            double[] energy = new double[n];
            double[] squared = new double[n];

            for (int i = 0; i < n; i++)
            {
                squared[i] = sig[i] * sig[i];
            }

            // 後ろから累積和
            double cumulative = 0.0;
            for (int i = n - 1; i >= 0; i--)
            {
                cumulative += squared[i];
                energy[i] = cumulative;
            }

            // return energy[0] + energy - 2 * autocorr
            double[] result = new double[n];
            for (int i = 0; i < n; i++)
            {
                result[i] = energy[0] + energy[i] - 2.0 * autocorr[i];
            }

            return result;
        }

        // cumulative_mean_normalized_difference
        public static double[] CumulativeMeanNormalizedDifference(double[] diff)
        {
            if (diff == null || diff.Length == 0)
                return diff;

            diff[0] = 1.0;
            double sumValue = 0.0;

            for (int tau = 1; tau < diff.Length; tau++)
            {
                sumValue += diff[tau];
                diff[tau] /= (sumValue / tau);
            }

            return diff;
        }
        private const double YIN_THRESHOLD = 0.3;

        public static int? AbsoluteThreshold(double[] diff, double threshold = YIN_THRESHOLD)
        {
            int tau = 2;
            while (tau < diff.Length)
            {
                if (diff[tau] < threshold)
                {
                    while (tau + 1 < diff.Length && diff[tau + 1] < diff[tau])
                    {
                        tau++;
                    }
                    break;
                }
                tau++;
            }

            if (tau >= diff.Length -1)
                return null;
            else if(diff[tau] >= threshold)
                return null;
            else
                return tau;
        }

        public static double[] InvertNsd(double[] nsd)
        {
            int tau = 0;
            while (tau < nsd.Length && nsd[tau] > 0)
            {
                nsd[tau] = 0;
                tau++;
                if (tau >= nsd.Length)
                    return null;
            }

            // Python の -nsd に対応（要素ごとに符号反転）
            double[] inverted = new double[nsd.Length];
            for (int i = 0; i < nsd.Length; i++)
            {
                inverted[i] = -nsd[i];
            }
            return inverted;
        }

        public static double YinNsdType1(double[] sig, int samplerate)
        {
            double[] nsd = NormalizedSquareDifferenceType1(sig);
            nsd = InvertNsd(nsd);
            if (nsd == null)
                return double.NaN;

            int? tau = AbsoluteThreshold(nsd, 0);
            if (tau == null)
                return double.NaN;

            return (double)samplerate / tau.Value;
        }

        public static double YinNsdType2(double[] sig, int samplerate)
        {
            double[] nsd = NormalizedSquareDifferenceType2(sig);
            nsd = InvertNsd(nsd);
            if (nsd == null)
                return double.NaN;

            int? tau = AbsoluteThreshold(nsd, 0);
            if (tau == null)
                return double.NaN;

            return (double)samplerate / tau.Value;
        }
        public static double YinType1(double[] sig, int samplerate)
        {
            double[] diff = DifferenceType1(sig);
            double[] cmnd = CumulativeMeanNormalizedDifference(diff);
            int? tau = AbsoluteThreshold(cmnd);
            if (tau == null)
                return double.NaN;
            var paraVal = ParabolicInterpolation(cmnd, tau.Value);
            if (paraVal == 0) return double.NaN;
            return (double)samplerate / paraVal;
        }

        public static double YinType2(double[] sig, int samplerate)
        {
            double[] diff = DifferenceType2(sig);
            double[] cmnd = CumulativeMeanNormalizedDifference(diff);
            double threshold = cmnd.Min() * 1.5;
            int? tau = AbsoluteThreshold(cmnd, threshold);
            if (tau == null)
                return double.NaN;
            var paraVal = ParabolicInterpolation(cmnd, tau.Value);
            if (paraVal == 0) return double.NaN;
            return (double)samplerate / paraVal;
        }
    }
}

