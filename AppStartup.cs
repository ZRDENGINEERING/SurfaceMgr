using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;

[assembly: ExtensionApplication(typeof(SurfaceMgr.AppStartup))]

namespace SurfaceMgr
{
    public class AppStartup : IExtensionApplication
    {
        public void Initialize()
        {
            var ed = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument.Editor;

            ed.WriteMessage("\n--- SurfaceMgr loaded ---");

            try
            {
                var commands = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .SelectMany(t => t.GetMethods())
                    .Select(m => m.GetCustomAttribute<CommandMethodAttribute>())
                    .Where(attr => attr != null)
                    .Select(attr => attr!.GlobalName)
                    .OrderBy(name => name)
                    .ToList();

                foreach (var cmd in commands)
                    ed.WriteMessage($"\n  {cmd}");
            }
            catch (ReflectionTypeLoadException ex)
            {
                ed.WriteMessage("\n(Could not list commands — one or more types failed to load)");
                foreach (var loaderEx in ex.LoaderExceptions)
                    ed.WriteMessage($"\n  {loaderEx?.Message}");
            }

            ed.WriteMessage("\n-------------------------\n");
        }

        public void Terminate() { }
    }
}