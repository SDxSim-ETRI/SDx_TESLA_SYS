using System;
using System.Runtime.InteropServices;
namespace SDx.Scenario1 {
  // These public C API calls do not duplicate the native struct layout.
  // Structs and model/data pointers are provided by the installed MuJoCo plugin.
  internal static unsafe class Scenario1Native {
    const string Library = "mujoco";
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern IntPtr mj_makeData(IntPtr model);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern void mj_deleteData(IntPtr data);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern IntPtr mj_copyData(IntPtr destination, IntPtr model, IntPtr source);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern void mj_kinematics(IntPtr model, IntPtr data);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern void mj_comPos(IntPtr model, IntPtr data);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern void mj_jac(IntPtr model, IntPtr data, double* positionJacobian,
      double* rotationJacobian, double* worldPoint, int body);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)]
    internal static extern IntPtr mj_versionString();
  }
}
