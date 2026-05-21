import { spawn, ChildProcess, execSync } from 'child_process'
import { join } from 'path'
import { createInterface } from 'readline'
import type { DeviceBatterySnapshot } from '../renderer/types'

interface JsonRpcRequest {
  id: string
  method: string
  params?: Record<string, unknown>
}

interface JsonRpcResponse {
  id: string
  ok: boolean
  result?: unknown
  error?: { code: string; message: string }
}

interface PendingRequest {
  resolve: (value: JsonRpcResponse) => void
  reject: (error: Error) => void
  timer: NodeJS.Timeout
}

const REQUEST_TIMEOUT = 10000
const RESTART_DELAY = 3000
const MAX_RESTART_ATTEMPTS = 3

export class HelperBridge {
  private process: ChildProcess | null = null
  private pending = new Map<string, PendingRequest>()
  private requestId = 0
  private restartAttempts = 0
  private buffer = ''
  private listeners = new Map<string, Array<(payload: unknown) => void>>()
  private onStatusChange?: (connected: boolean) => void

  constructor(private helperProjectPath: string) {}

  setStatusCallback(callback: (connected: boolean) => void): void {
    this.onStatusChange = callback
  }

  start(): void {
    if (this.process) return

    try {
      // Build the helper first, then run the exe directly to avoid
      // dotnet run stdout interference with JSON-RPC communication.
      const exePath = join(this.helperProjectPath, 'bin', 'Debug', 'net8.0-windows10.0.19041.0', 'AirVolt.NativeHelper.exe')
      console.log('[HelperBridge] building helper...')
      execSync('dotnet build --nologo -v q', { cwd: this.helperProjectPath, stdio: 'pipe', timeout: 30000 })
      console.log('[HelperBridge] starting exe:', exePath)
      this.process = spawn(exePath, [], {
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true
      })

      this.process.on('error', (err) => {
        console.error('[HelperBridge] process error:', err.message)
        this.handleCrash()
      })

      this.process.on('exit', (code, signal) => {
        console.warn(`[HelperBridge] process exited code=${code} signal=${signal}`)
        this.process = null
        this.rejectAllPending(new Error('Helper process exited'))
        this.handleCrash()
      })

      if (this.process.stdout) {
        const rl = createInterface({ input: this.process.stdout })
        rl.on('line', (line: string) => {
          this.handleLine(line)
        })
      }

      if (this.process.stderr) {
        const rl = createInterface({ input: this.process.stderr })
        rl.on('line', (line: string) => {
          console.log('[Helper]', line)
        })
      }

      console.log('[HelperBridge] started helper process')
    } catch (err) {
      console.error('[HelperBridge] failed to start:', err)
      this.onStatusChange?.(false)
    }
  }

  private handleLine(line: string): void {
    console.log('[HelperBridge] stdout:', line)
    try {
      const msg = JSON.parse(line)
      if ('event' in msg) {
        const handlers = this.listeners.get(msg.event)
        if (handlers) {
          for (const h of handlers) {
            h(msg.payload)
          }
        }
      } else if ('id' in msg) {
        console.log('[HelperBridge] response id:', msg.id, 'pending keys:', [...this.pending.keys()])
        const pending = this.pending.get(msg.id)
        if (pending) {
          console.log('[HelperBridge] resolving pending request:', msg.id)
          clearTimeout(pending.timer)
          this.pending.delete(msg.id)
          pending.resolve(msg as JsonRpcResponse)
        } else {
          console.log('[HelperBridge] no pending request for id:', msg.id)
        }
      } else {
        console.log('[HelperBridge] unknown message (no event or id):', Object.keys(msg))
      }
    } catch (e) {
      console.log('[HelperBridge] parse error:', e, 'line:', line.substring(0, 200))
      this.buffer += line
    }
  }

  async send(method: string, params?: Record<string, unknown>): Promise<JsonRpcResponse> {
    if (!this.process || this.process.exitCode !== null) {
      throw new Error('Helper process not running')
    }

    const id = String(++this.requestId)
    const request: JsonRpcRequest = { id, method, params }

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`Request ${method} timed out`))
      }, REQUEST_TIMEOUT)

      this.pending.set(id, { resolve, reject, timer })

      try {
        const payload = JSON.stringify(request) + '\n'
        console.log('[HelperBridge] writing to stdin:', payload.trim())
        this.process!.stdin!.write(payload, (err) => {
          if (err) console.error('[HelperBridge] stdin write error:', err)
        })
      } catch (err) {
        clearTimeout(timer)
        this.pending.delete(id)
        reject(err)
      }
    })
  }

  on(event: string, handler: (payload: unknown) => void): void {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, [])
    }
    this.listeners.get(event)!.push(handler)
  }

  async scanDevices(): Promise<DeviceBatterySnapshot[]> {
    const res = await this.send('devices.scan', { includeUnsupported: true })
    if (!res.ok) throw new Error(res.error?.message ?? 'Scan failed')
    const result = res.result as { devices: DeviceBatterySnapshot[] }
    return result.devices
  }

  async healthCheck(): Promise<{ version: string }> {
    const res = await this.send('helper.health')
    if (!res.ok) throw new Error(res.error?.message ?? 'Health check failed')
    return res.result as { version: string }
  }

  private handleCrash(): void {
    this.onStatusChange?.(false)
    if (this.restartAttempts >= MAX_RESTART_ATTEMPTS) {
      console.error('[HelperBridge] max restart attempts reached')
      return
    }
    this.restartAttempts++
    console.log(`[HelperBridge] restarting in ${RESTART_DELAY}ms (attempt ${this.restartAttempts})`)
    setTimeout(() => {
      this.start()
      if (this.process) {
        this.restartAttempts = 0
      }
    }, RESTART_DELAY)
  }

  private rejectAllPending(error: Error): void {
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timer)
      pending.reject(error)
      this.pending.delete(id)
    }
  }

  stop(): void {
    if (this.process) {
      this.process.kill()
      this.process = null
    }
    this.rejectAllPending(new Error('Helper stopped'))
  }
}
