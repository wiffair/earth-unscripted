import AppKit
import Foundation
import GameController
import Darwin

struct Choice: Decodable { let title: String; let detail: String; let path: String; let identifier: String }
struct Manifest: Decodable { let version: String; let source: String; let choices: [Choice] }
final class GraphicsLauncher: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var buttons: [NSButton] = []
    var manifest: Manifest!
    var selected = 0
    var opening = false
    var lockFD: Int32 = -1
    var proofProfile: String?
    func applicationDidFinishLaunching(_ notification: Notification) {
        let lock = FileManager.default.temporaryDirectory.appendingPathComponent("RoadRushBetaLauncher-current.lock")
        lockFD = open(lock.path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else { NSApp.terminate(nil); return }
        do {
            guard let url = Bundle.main.url(forResource: "LaunchManifest", withExtension: "json") else { throw NSError(domain: "RoadRush", code: 1, userInfo: [NSLocalizedDescriptionKey:"The launcher configuration is missing."]) }
            manifest = try JSONDecoder().decode(Manifest.self, from: Data(contentsOf: url))
            guard manifest.choices.count == 2 else { throw NSError(domain: "RoadRush", code: 2) }
            if let index = CommandLine.arguments.firstIndex(of: "-roadRushLauncherProof"), index + 1 < CommandLine.arguments.count {
                let value = CommandLine.arguments[index + 1]
                guard value.count == 32, UUID(uuidString: formatUUID(value)) != nil else { throw NSError(domain: "RoadRush", code: 3) }
                proofProfile = value
            }
        } catch { showError(error.localizedDescription); NSApp.terminate(nil); return }
        window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 720,height: 410), styleMask: [.titled,.closable,.miniaturizable], backing: .buffered, defer: false)
        window.title = "Earth Unscripted · Choose graphics"
        window.center()
        let content = window.contentView!
        content.wantsLayer = true
        content.layer?.backgroundColor = NSColor(srgbRed:0.025,green:0.053,blue:0.08,alpha:1).cgColor
        window.appearance = NSAppearance(named:.darkAqua)
        if let iconURL=Bundle.main.url(forResource:"AppIcon",withExtension:"icns"),let icon=NSImage(contentsOf:iconURL) { let view=NSImageView(frame:NSRect(x:590,y:285,width:96,height:96));view.image=icon;view.imageScaling = .scaleProportionallyUpOrDown;content.addSubview(view) }
        label("Earth Unscripted",frame:NSRect(x:28,y:310,width:550,height:58),size:36,bold:true,content:content)
        label("Choose your graphics", frame: NSRect(x: 28,y: 263,width: 590,height: 35), size: 22, bold: true, content: content)
        label("Both versions use the same cars, progress and saved locations.", frame: NSRect(x: 28,y: 237,width: 585,height: 35), size: 14, bold: false, content: content)
        for (index, choice) in manifest.choices.enumerated() {
            let button = NSButton(frame: NSRect(x: 28 + index*340,y: 118,width: 320,height: 104))
            button.title = choice.title + "\n" + choice.detail
            button.bezelStyle = .regularSquare; button.font = NSFont.systemFont(ofSize: 16, weight: .semibold)
            button.target = self; button.action = #selector(choose(_:)); button.tag = index
            button.setAccessibilityLabel(choice.title + ". " + choice.detail)
            button.focusRingType = .exterior
            content.addSubview(button); buttons.append(button)
        }
        label("HD adds richer lighting and reflections and needs more graphics power.\nYou can choose again each time you launch.", frame: NSRect(x: 28,y: 58,width: 585,height: 46), size: 13, bold: false, content: content)
        let cancel = NSButton(frame: NSRect(x: 28,y: 18,width: 104,height: 30));cancel.title = "Cancel";cancel.bezelStyle = .rounded;cancel.target = self;cancel.action = #selector(cancelLaunch);cancel.keyEquivalent = "\u{1b}";content.addSubview(cancel)
        selected = UserDefaults.standard.string(forKey: "graphicsChoice") == "hd" ? 1 : 0
        focus(selected)
        NotificationCenter.default.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { [weak self] _ in self?.bindControllers() }
        bindControllers();window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps: true)
    }
    func formatUUID(_ token: String) -> String {
        let c = Array(token);return [String(c[0..<8]),String(c[8..<12]),String(c[12..<16]),String(c[16..<20]),String(c[20..<32])].joined(separator:"-")
    }
    func label(_ text: String, frame: NSRect, size: CGFloat, bold: Bool, content: NSView) {
        let value = NSTextField(wrappingLabelWithString: text);value.frame = frame;value.font = NSFont.systemFont(ofSize: size, weight: bold ? .semibold : .regular);value.textColor=NSColor(srgbRed:0.96,green:0.94,blue:0.87,alpha:1);content.addSubview(value)
    }
    func focus(_ index: Int) { selected = index;window?.makeFirstResponder(buttons[index]);buttons[index].keyEquivalent = "\r";buttons[1-index].keyEquivalent = "" }
    func bindControllers() {
        for controller in GCController.controllers() {
            guard let gamepad = controller.extendedGamepad else { continue }
            gamepad.dpad.valueChangedHandler = { [weak self] _, x, y in if abs(x)>0.5 || abs(y)>0.5 { DispatchQueue.main.async { self?.focus(x>0 || y<0 ? 1:0) } } }
            gamepad.leftThumbstick.valueChangedHandler = gamepad.dpad.valueChangedHandler
            gamepad.buttonA.pressedChangedHandler = { [weak self] _, _, pressed in if pressed { DispatchQueue.main.async { guard let s=self else{return};s.choose(s.buttons[s.selected]) } } }
            gamepad.buttonB.pressedChangedHandler = { _,_,pressed in if pressed { DispatchQueue.main.async { NSApp.terminate(nil) } } }
        }
    }
    @objc func cancelLaunch() { NSApp.terminate(nil) }
    @objc func choose(_ sender: NSButton) {
        guard !opening else { return }
        selected = sender.tag
        let choice = manifest.choices[selected]
        let target = (choice.path.hasPrefix("/") ? URL(fileURLWithPath: choice.path) : Bundle.main.resourceURL!.appendingPathComponent(choice.path)).standardizedFileURL
        guard let bundle = Bundle(url: target), bundle.bundleIdentifier == choice.identifier,
              bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == manifest.version else {
            showError("This graphics version is missing or out of date. Reinstall the matching beta package.");return
        }
        do {
            let sourceURL = target.appendingPathComponent("Contents/Resources/Data/StreamingAssets/SharedGameSource.json")
            let source = try JSONSerialization.jsonObject(with: Data(contentsOf: sourceURL)) as? [String:Any]
            guard source?["source"] as? String == manifest.source else { showError("These graphics versions are from different game updates. Install the matching pair before playing.");return }
        } catch { showError("This graphics version could not be verified. Reinstall the matching beta package.");return }
        let identities = Set(manifest.choices.map { $0.identifier })
        let running = NSWorkspace.shared.runningApplications.filter { identities.contains($0.bundleIdentifier ?? "") }
        if let existing = running.first(where: { $0.bundleURL?.standardizedFileURL == target }), running.count == 1 {
            existing.activate(options: []);NSApp.terminate(nil);return
        }
        if !running.isEmpty {
            let alert = NSAlert();alert.messageText = "Switch graphics version?";alert.informativeText = "Earth Unscripted will save and close the current drive, then open your chosen graphics version.";alert.addButton(withTitle: "Save and switch");alert.addButton(withTitle: "Cancel")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
            guard running.allSatisfy({ $0.terminate() }) else { showError("The current game could not close safely. Quit it and try again.");return }
        }
        opening = true;buttons.forEach { $0.isEnabled = false }
        waitForClose(running, target, deadline: Date().addingTimeInterval(20))
    }
    func waitForClose(_ running: [NSRunningApplication], _ target: URL, deadline: Date) {
        if running.contains(where: { !$0.isTerminated }) {
            if Date() > deadline { opening = false;buttons.forEach{$0.isEnabled=true};showError("The current game is still saving or closing. Wait for it to finish, then try again.");return }
            DispatchQueue.main.asyncAfter(deadline: .now()+0.2) { [weak self] in self?.waitForClose(running,target,deadline:deadline) };return
        }
        UserDefaults.standard.set(selected == 1 ? "hd":"standard",forKey:"graphicsChoice")
        let configuration = NSWorkspace.OpenConfiguration();configuration.createsNewApplicationInstance = true;configuration.activates = true
        if let profile = proofProfile {
            configuration.arguments = ["-roadRushBetaSmoke",UUID().uuidString.replacingOccurrences(of:"-",with:"").lowercased(),"-roadRushSharedProfile",profile,"-roadRushGraphicsProof","-screen-fullscreen","0","-screen-width","1440","-screen-height","900"]
        } else {
            // Isolated QA uses a small window. Normal launches return to the
            // current display rather than inheriting those test dimensions.
            let screen = NSScreen.main
            let size = screen?.frame.size ?? NSSize(width:1440,height:900)
            let scale = screen?.backingScaleFactor ?? 1
            configuration.arguments = ["-screen-fullscreen","1","-screen-width",String(Int(size.width*scale)),"-screen-height",String(Int(size.height*scale))]
        }
        NSWorkspace.shared.openApplication(at: target, configuration: configuration) { [weak self] _, error in
            DispatchQueue.main.async {
                if let error = error { self?.opening = false;self?.buttons.forEach{$0.isEnabled=true};self?.showError(error.localizedDescription) }
                else { NSApp.terminate(nil) }
            }
        }
    }
    func showError(_ message: String) { let alert = NSAlert();alert.messageText = "Earth Unscripted Beta";alert.informativeText = message;alert.addButton(withTitle:"OK");alert.runModal() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ notification: Notification) { if lockFD >= 0 { flock(lockFD,LOCK_UN);close(lockFD) } }
}
let app = NSApplication.shared
app.setActivationPolicy(.regular)
let delegate = GraphicsLauncher();app.delegate = delegate
app.run()
