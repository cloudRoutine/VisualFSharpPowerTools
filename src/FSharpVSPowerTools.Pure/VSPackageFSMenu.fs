
namespace VSPackageFSMenu

module Guids =
  [<Literal>]
  let GuidVSPackageFSMenuPkgString = "68b42cfe-c752-4094-8dba-ed48aa81cac8"

  [<Literal>]
  let GuidVSPackageFSMenuCmdSetString = "ef22ea03-fe4c-4752-abdb-9c2caffc923c"

  let guidVSPackageFSMenuCmdSet = System.Guid GuidVSPackageFSMenuCmdSetString

module CommandIDs =
  let cmdidMyCommandFSMenu = 0x100

[<Microsoft.VisualStudio.Shell.PackageRegistration (UseManagedResourcesOnly=true)>]
[<Microsoft.VisualStudio.Shell.InstalledProductRegistration ("#110", "#112", "1.0", IconResourceID = 400)>]
[<Microsoft.VisualStudio.Shell.ProvideMenuResource ("Menus.ctmenu", 1)>]
[<System.Runtime.InteropServices.Guid (Guids.GuidVSPackageFSMenuPkgString)>]


type MyPackage () =
    inherit Microsoft.VisualStudio.Shell.Package ()

    let menuExecuteHandler = 
        System.EventHandler 
            (fun _ _ -> System.Windows.Forms.MessageBox.Show "Inside F# function!" |> ignore)

    override __.Initialize() =
        base.Initialize()
        let menuService =
            base.GetService typeof<System.ComponentModel.Design.IMenuCommandService> 
                :?> Microsoft.VisualStudio.Shell.OleMenuCommandService

        let commandId   = System.ComponentModel.Design.CommandID (Guids.guidVSPackageFSMenuCmdSet, CommandIDs.cmdidMyCommandFSMenu)
        let menuCommand = System.ComponentModel.Design.MenuCommand (menuExecuteHandler, commandId)
        
        menuService.AddCommand menuCommand