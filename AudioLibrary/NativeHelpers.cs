using MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AudioLibrary
{
    class NativeHelpers
    {
        internal static void CheckHr(HResult hr, string message)
        {
            if (hr != 0)
            {
                throw new COMException(message + " (" + hr.ToString() + ")", (int)hr);
            }
        }
    }
}
