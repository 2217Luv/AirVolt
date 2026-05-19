import { Tray, Menu, BrowserWindow, nativeImage, screen } from 'electron'
import { join } from 'path'

let tray: Tray | null = null
let popupWindow: BrowserWindow | null = null

function createTrayIcon(): nativeImage {
  const size = 16
  const buf = Buffer.alloc(size * size * 4)
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const i = (y * size + x) * 4
      const inBatteryBody = x >= 2 && x <= 11 && y >= 3 && y <= 13
      const inBatteryCap = x >= 12 && x <= 13 && y >= 6 && y <= 10
      if (inBatteryBody || inBatteryCap) {
        buf[i] = 76      // R
        buf[i + 1] = 175 // G
        buf[i + 2] = 80  // B
        buf[i + 3] = 255 // A
      } else {
        buf[i] = 0
        buf[i + 1] = 0
        buf[i + 2] = 0
        buf[i + 3] = 0
      }
    }
  }
  return nativeImage.createFromBuffer(buf, { width: size, height: size, scaleFactor: 2 })
}

export function createTray(getDevicesCb: () => unknown): Tray {
  const icon = createTrayIcon()
  tray = new Tray(icon)
  tray.setToolTip('AirVolt')

  const contextMenu = Menu.buildFromTemplate([
    { label: 'Show Devices', click: () => togglePopup(getDevicesCb) },
    { type: 'separator' },
    { label: 'Settings', click: () => openSettingsWindow() },
    { type: 'separator' },
    { label: 'Quit', click: () => { closePopup(); require('electron').app.quit() } }
  ])

  tray.setContextMenu(contextMenu)
  tray.on('click', () => togglePopup(getDevicesCb))

  return tray
}

function getPopupPosition(): { x: number; y: number } {
  const trayBounds = tray!.getBounds()
  const primaryDisplay = screen.getPrimaryDisplay()
  const workArea = primaryDisplay.workArea

  const popupWidth = 320
  const popupHeight = 480

  let x = trayBounds.x + trayBounds.width / 2 - popupWidth / 2
  const y = workArea.y + 4

  if (x + popupWidth > workArea.x + workArea.width) {
    x = workArea.x + workArea.width - popupWidth - 8
  }
  if (x < workArea.x) {
    x = workArea.x + 8
  }

  return { x: Math.round(x), y: Math.round(y) }
}

function createPopupWindow(): BrowserWindow {
  const { x, y } = getPopupPosition()
  popupWindow = new BrowserWindow({
    width: 320,
    height: 480,
    x,
    y,
    frame: false,
    resizable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    show: false,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false
    }
  })

  popupWindow.on('blur', () => {
    closePopup()
  })

  if (import.meta.env.DEV) {
    popupWindow.loadURL(process.env['ELECTRON_RENDERER_URL']!)
  } else {
    popupWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }

  return popupWindow
}

function togglePopup(getDevicesCb: () => unknown): void {
  if (popupWindow && !popupWindow.isDestroyed()) {
    closePopup()
    return
  }

  createPopupWindow()
  popupWindow!.once('ready-to-show', () => {
    popupWindow!.show()
    getDevicesCb()
  })
}

function closePopup(): void {
  if (popupWindow && !popupWindow.isDestroyed()) {
    popupWindow.close()
    popupWindow = null
  }
}

function openSettingsWindow(): void {
  closePopup()
  const settingsWin = new BrowserWindow({
    width: 420,
    height: 520,
    resizable: false,
    title: 'AirVolt Settings',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false
    }
  })

  settingsWin.setMenuBarVisibility(false)

  if (import.meta.env.DEV) {
    settingsWin.loadURL(process.env['ELECTRON_RENDERER_URL']! + '#settings')
  } else {
    settingsWin.loadFile(join(__dirname, '../renderer/index.html'), { hash: '#settings' })
  }
}

export { closePopup, openSettingsWindow }
