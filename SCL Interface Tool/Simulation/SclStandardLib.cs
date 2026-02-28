using System;
using System.Collections.Generic;

namespace SCL_Interface_Tool.Simulation
{
    public static class SclStandardLib
    {
        public static bool UseVirtualTime = false;
        public static long VirtualTickCount = 0;

        public static long GetTickCount()
        {
            return UseVirtualTime ? VirtualTickCount : Environment.TickCount64;
        }

        #region --- Math Functions ---
        public static float ABS(float val) => Math.Abs(val);
        public static int ABS(int val) => Math.Abs(val);
        public static float SQRT(float val) => (float)Math.Sqrt(val);
        public static float LN(float val) => (float)Math.Log(val);
        public static float LOG(float val) => (float)Math.Log10(val);
        public static float EXP(float val) => (float)Math.Exp(val);
        public static float EXPD(float val) => (float)Math.Pow(10, val);
        public static float SIN(float val) => (float)Math.Sin(val);
        public static float COS(float val) => (float)Math.Cos(val);
        public static float TAN(float val) => (float)Math.Tan(val);
        public static float ASIN(float val) => (float)Math.Asin(val);
        public static float ACOS(float val) => (float)Math.Acos(val);
        public static float ATAN(float val) => (float)Math.Atan(val);
        public static float EXPT(float bas, float exp) => (float)Math.Pow(bas, exp);
        #endregion

        #region --- Selection & Comparison ---
        public static float NORM_X(float min, float value, float max)
        {
            if (max == min) return 0f;
            return Math.Clamp((value - min) / (max - min), 0f, 1f);
        }

        public static float SCALE_X(float min, float value, float max)
        {
            return Math.Clamp(value, 0f, 1f) * (max - min) + min;
        }

        public static float LIMIT(float mn, float val, float mx) => Math.Clamp(val, mn, mx);
        public static int LIMIT(int mn, int val, int mx) => Math.Clamp(val, mn, mx);

        public static float SEL(bool g, float in0, float in1) => g ? in1 : in0;
        public static int SEL(bool g, int in0, int in1) => g ? in1 : in0;
        public static bool SEL(bool g, bool in0, bool in1) => g ? in1 : in0;

        public static float MAX(float a, float b) => Math.Max(a, b);
        public static int MAX(int a, int b) => Math.Max(a, b);
        public static float MIN(float a, float b) => Math.Min(a, b);
        public static int MIN(int a, int b) => Math.Min(a, b);

        public static int MUX(int k, params int[] values) => (k >= 0 && k < values.Length) ? values[k] : 0;
        public static float MUX(int k, params float[] values) => (k >= 0 && k < values.Length) ? values[k] : 0f;
        #endregion

        #region --- Bitwise / Shift ---
        public static int SHL(int val, int n) => val << n;
        public static int SHR(int val, int n) => val >> n;
        public static int ROL(int val, int n) => (val << n) | (val >> (32 - n));
        public static int ROR(int val, int n) => (val >> n) | (val << (32 - n));
        #endregion

        #region --- Type Conversions ---
        public static int BOOL_TO_INT(bool val) => val ? 1 : 0;
        public static bool INT_TO_BOOL(int val) => val != 0;
        public static float INT_TO_REAL(int val) => (float)val;
        public static int REAL_TO_INT(float val) => (int)val;
        public static float DINT_TO_REAL(int val) => (float)val;
        public static int REAL_TO_DINT(float val) => (int)val;
        public static int BOOL_TO_WORD(bool val) => val ? 1 : 0;
        public static int BYTE_TO_INT(int val) => val & 0xFF;
        public static int WORD_TO_INT(int val) => val & 0xFFFF;
        public static int INT_TO_WORD(int val) => val & 0xFFFF;
        public static int DWORD_TO_INT(int val) => val;
        public static int INT_TO_DWORD(int val) => val;
        public static int TIME_TO_INT(int val) => val;
        public static int INT_TO_TIME(int val) => val;
        public static int TIME_TO_DINT(int val) => val;
        public static int DINT_TO_TIME(int val) => val;

        public static long DATE_TO_DINT(long ticks) => ticks;
        public static long DINT_TO_DATE(int val) => val;
        public static long TOD_TO_DINT(long ticks) => ticks;
        public static long DT_TO_DATE(long dtTicks) => new DateTime(dtTicks).Date.Ticks;
        public static long DT_TO_TOD(long dtTicks) => new DateTime(dtTicks).TimeOfDay.Ticks;
        #endregion

        #region --- Date/Time Helper Functions ---
        public static int DATE_YEAR(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Year;
        public static int DATE_MONTH(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Month;
        public static int DATE_DAY(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Day;
        public static int TOD_HOUR(long ticks) => (int)(new TimeSpan(ticks).TotalHours);
        public static int TOD_MINUTE(long ticks) => new TimeSpan(ticks).Minutes;
        public static int TOD_SECOND(long ticks) => new TimeSpan(ticks).Seconds;
        public static int DT_HOUR(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Hour;
        public static int DT_MINUTE(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Minute;
        public static int DT_SECOND(long ticks) => ticks == 0 ? 0 : new DateTime(ticks).Second;

        public static long CONCAT_DATE_TOD(long dateTicks, long todTicks)
        {
            if (dateTicks == 0) return todTicks;
            DateTime d = new DateTime(dateTicks).Date;
            TimeSpan t = new TimeSpan(todTicks);
            return (d + t).Ticks;
        }
        #endregion

        #region --- String Functions ---
        public static int LEN(string s) => s?.Length ?? 0;
        public static string LEFT(string s, int l) => s?.Substring(0, Math.Min(l, s.Length)) ?? "";
        public static string RIGHT(string s, int l) => s?.Substring(Math.Max(0, s.Length - l)) ?? "";
        public static string MID(string s, int l, int p) => (s != null && p >= 1 && p <= s.Length) ? s.Substring(p - 1, Math.Min(l, s.Length - p + 1)) : "";
        public static string CONCAT(string s1, string s2) => (s1 ?? "") + (s2 ?? "");
        public static int FIND(string s1, string s2) => s1 != null && s2 != null ? s1.IndexOf(s2, StringComparison.Ordinal) + 1 : 0;
        public static string INSERT(string s1, string s2, int p) => s1 != null && p >= 1 ? s1.Insert(Math.Min(p - 1, s1.Length), s2 ?? "") : s1 ?? "";
        public static string DELETE(string s1, int l, int p) => s1 != null && p >= 1 ? s1.Remove(Math.Min(p - 1, s1.Length), Math.Min(l, s1.Length - p + 1)) : s1 ?? "";
        public static string REPLACE(string s1, string s2, int l, int p) => s1 != null && p >= 1 ? s1.Remove(p - 1, Math.Min(l, s1.Length - p + 1)).Insert(p - 1, s2 ?? "") : s1 ?? "";
        #endregion

        #region --- Array Helpers ---
        public static object ARRAY_GET(Dictionary<int, object> arr, int idx) => arr.ContainsKey(idx) ? arr[idx] : 0;
        public static void ARRAY_SET(Dictionary<int, object> arr, int idx, object val) { arr[idx] = val; }
        #endregion

        #region --- Struct Helpers ---
        public static object STRUCT_GET(Dictionary<string, object> s, string field) => s.ContainsKey(field) ? s[field] : 0;
        public static void STRUCT_SET(Dictionary<string, object> s, string field, object val) { s[field] = val; }
        #endregion

        #region --- Hardware Timers & Triggers ---
        public class R_TRIG
        {
            private bool _prev;
            public bool Q { get; private set; }
            public void Execute(bool CLK) { Q = CLK && !_prev; _prev = CLK; }
        }

        public class F_TRIG
        {
            private bool _prev;
            public bool Q { get; private set; }
            public void Execute(bool CLK) { Q = !CLK && _prev; _prev = CLK; }
        }

        public class SR
        {
            public bool Q1 { get; private set; }
            public void Execute(bool S1, bool R)
            {
                Q1 = (S1 || Q1) && !R;
                if (S1) Q1 = true;
            }
        }

        public class RS
        {
            public bool Q1 { get; private set; }
            public void Execute(bool S, bool R1)
            {
                Q1 = (S || Q1) && !R1;
            }
        }

        public class CTU
        {
            private bool _prevCU;
            public int CV { get; private set; }
            public bool Q { get; private set; }
            public void Execute(bool CU, bool R, int PV)
            {
                if (R) { CV = 0; } else if (CU && !_prevCU) { CV++; }
                _prevCU = CU; Q = CV >= PV;
            }
        }

        public class CTD
        {
            private bool _prevCD;
            public int CV { get; private set; }
            public bool Q { get; private set; }
            public void Execute(bool CD, bool LD, int PV)
            {
                if (LD) { CV = PV; } else if (CD && !_prevCD) { CV--; }
                _prevCD = CD; Q = CV <= 0;
            }
        }

        public class CTUD
        {
            private bool _prevCU, _prevCD;
            public int CV { get; private set; }
            public bool QU { get; private set; }
            public bool QD { get; private set; }
            public void Execute(bool CU, bool CD, bool R, bool LD, int PV)
            {
                if (R) { CV = 0; }
                else if (LD) { CV = PV; }
                else { if (CU && !_prevCU) CV++; if (CD && !_prevCD) CV--; }
                _prevCU = CU; _prevCD = CD; QU = CV >= PV; QD = CV <= 0;
            }
        }

        public class TON
        {
            private long _startTime; private bool _timing;
            public bool Q { get; private set; }
            public int ET { get; private set; }
            public void Execute(bool IN, int PT)
            {
                if (IN)
                {
                    if (!_timing) { _startTime = GetTickCount(); _timing = true; }
                    ET = (int)Math.Min(GetTickCount() - _startTime, PT); Q = ET >= PT;
                }
                else { Q = false; ET = 0; _timing = false; }
            }
        }

        public class TOF
        {
            private long _startTime; private bool _timing;
            public bool Q { get; private set; }
            public int ET { get; private set; }
            public void Execute(bool IN, int PT)
            {
                if (IN) { Q = true; ET = 0; _timing = false; }
                else
                {
                    if (!_timing && Q) { _startTime = GetTickCount(); _timing = true; }
                    if (_timing)
                    {
                        ET = (int)Math.Min(GetTickCount() - _startTime, PT);
                        if (ET >= PT) { Q = false; _timing = false; }
                    }
                }
            }
        }

        public class TP
        {
            private long _startTime;
            private bool _timing;
            private bool _prevIn;

            public bool Q { get; private set; }
            public int ET { get; private set; }

            public void Execute(bool IN, int PT)
            {
                if (IN && !_prevIn && !_timing)
                {
                    _timing = true;
                    Q = true;
                    _startTime = GetTickCount();
                    ET = 0;
                }

                if (_timing)
                {
                    ET = (int)Math.Min(GetTickCount() - _startTime, PT);
                    if (ET >= PT)
                    {
                        _timing = false;
                        Q = false;
                    }
                }

                if (!_timing && !IN)
                {
                    ET = 0;
                }

                _prevIn = IN;
            }
        }

        public class TONR
        {
            private long _lastTick; private bool _prevIn;
            public bool Q { get; private set; }
            public int ET { get; private set; }
            public void Execute(bool IN, bool R, int PT)
            {
                if (R) { Q = false; ET = 0; _prevIn = IN; return; }
                if (IN)
                {
                    if (!_prevIn) _lastTick = GetTickCount();
                    long now = GetTickCount(); ET = (int)Math.Min(ET + (now - _lastTick), PT);
                    _lastTick = now; Q = ET >= PT;
                }
                _prevIn = IN;
            }
        }
        #endregion
    }
}
