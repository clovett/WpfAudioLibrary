using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfAudioTool.Utilities
{
    class Numeric
    {
        public static List<float> Summarize(List<float> data, int width, Func<float, float, float> summaryFunc)
        {
            List<float> summary = new List<float>(width);
            int len = data.Count;
            double step = (double)len / (double)width;
            double pos = 0;
            while (pos + step < len)
            {
                int i = (int)pos;
                float value = data[i];
                for (int j = 1; j < step; j++)
                {
                    value = summaryFunc(value, data[i + j]);
                }
                summary.Add(value);
                pos += step;
            }
            return summary;
        }

    }
}
