import { Component, OnDestroy, signal } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-voice',
  standalone: true,
  imports: [NgIf, FormsModule],
  template: `
    <div class="voice-page">
      <div class="page-header">
        <div class="header-content">
          <h1>Voice Assistant</h1>
          <p>Speak to your AI agent or type commands naturally</p>
        </div>
      </div>

      <div class="voice-container">
        <div class="voice-main">
          <div class="voice-visual" [class.recording]="isRecording()" [class.processing]="processing()">
            <div class="pulse-ring" *ngIf="isRecording()"></div>
            <div class="pulse-ring delay" *ngIf="isRecording()"></div>
            <button class="mic-btn" (click)="toggleRecording()" [class.active]="isRecording()" [class.processing]="processing()">
              <span class="material-icons">{{ isRecording() ? 'stop' : (processing() ? 'hourglass_empty' : 'mic') }}</span>
            </button>
            <p class="voice-status">{{ getStatusText() }}</p>
          </div>

          <div class="waveform" *ngIf="isRecording()">
            <div class="bar" *ngFor="let b of bars; let i = index" [style.height.px]="b" [style.animation-delay]="i * 0.05 + 's'"></div>
          </div>

          <div class="transcript-box" *ngIf="transcript()">
            <div class="box-header">
              <span class="material-icons">record_voice_over</span>
              <h4>Transcription</h4>
            </div>
            <p class="transcript-text">{{ transcript() }}</p>
          </div>

          <div class="result-box" *ngIf="result()">
            <div class="box-header">
              <span class="material-icons">smart_toy</span>
              <h4>AI Response</h4>
            </div>
            <p class="result-text">{{ result() }}</p>
          </div>
        </div>

        <div class="voice-sidebar">
          <div class="sidebar-card">
            <div class="card-header">
              <span class="material-icons">keyboard</span>
              <h4>Type a Command</h4>
            </div>
            <div class="manual-input">
              <input type="text" [(ngModel)]="manualText" placeholder="e.g., Create a task for tomorrow..." (keyup.enter)="sendCommand()" />
              <button class="send-btn" (click)="sendCommand()" [disabled]="processing() || !manualText.trim()">
                <span class="material-icons">send</span>
              </button>
            </div>
          </div>

          <div class="sidebar-card">
            <div class="card-header">
              <span class="material-icons">upload_file</span>
              <h4>Upload Audio</h4>
            </div>
            <label class="upload-area">
              <input type="file" (change)="onAudioUpload($event)" accept="audio/*" hidden />
              <span class="material-icons">audio_file</span>
              <span>Click to choose audio file</span>
              <span class="upload-hint">Supports MP3, WAV, WebM, OGG</span>
            </label>
          </div>

          <div class="sidebar-card">
            <div class="card-header">
              <span class="material-icons">tips_and_updates</span>
              <h4>Try Saying</h4>
            </div>
            <div class="suggestions">
              <button class="suggestion" (click)="quickCommand('Schedule a meeting tomorrow at 3pm')">
                <span class="material-icons">event</span> Schedule a meeting
              </button>
              <button class="suggestion" (click)="quickCommand('Create a task to review the report')">
                <span class="material-icons">check_circle</span> Create a task
              </button>
              <button class="suggestion" (click)="quickCommand('Check my emails')">
                <span class="material-icons">email</span> Check emails
              </button>
              <button class="suggestion" (click)="quickCommand('What is on my calendar today?')">
                <span class="material-icons">calendar_today</span> Check calendar
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .voice-page { padding: 2rem; }
    .page-header { margin-bottom: 2rem; }
    .header-content h1 { margin: 0; color: var(--text-primary); font-size: 1.8rem; }
    .header-content p { margin: 0.3rem 0 0; color: var(--text-secondary); }
    .voice-container { display: grid; grid-template-columns: 1fr 380px; gap: 2rem; align-items: start; }
    .voice-main { display: flex; flex-direction: column; align-items: center; }
    .voice-visual { position: relative; padding: 4rem 2rem; text-align: center; }
    .pulse-ring { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); width: 140px; height: 140px; border-radius: 50%; border: 2px solid rgba(102, 126, 234, 0.3); animation: pulse-ring 2s ease-out infinite; }
    .pulse-ring.delay { animation-delay: 0.5s; }
    @keyframes pulse-ring { 0% { transform: translate(-50%, -50%) scale(0.8); opacity: 1; } 100% { transform: translate(-50%, -50%) scale(2); opacity: 0; } }
    .mic-btn { width: 120px; height: 120px; border-radius: 50%; border: none; background: linear-gradient(135deg, #667eea, #764ba2); color: white; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.3s; position: relative; z-index: 1; box-shadow: 0 8px 30px rgba(102, 126, 234, 0.3); }
    .mic-btn .material-icons { font-size: 48px; }
    .mic-btn:hover { transform: scale(1.08); box-shadow: 0 12px 40px rgba(102, 126, 234, 0.5); }
    .mic-btn.active { background: linear-gradient(135deg, #f5576c, #ff6b6b); animation: pulse-btn 1.5s infinite; box-shadow: 0 8px 30px rgba(245, 87, 108, 0.4); }
    .mic-btn.processing { background: linear-gradient(135deg, #ffa726, #ff7043); animation: spin-icon 1s linear infinite; }
    @keyframes pulse-btn { 0%, 100% { box-shadow: 0 0 0 0 rgba(245, 87, 108, 0.4); } 50% { box-shadow: 0 0 0 20px rgba(245, 87, 108, 0); } }
    @keyframes spin-icon { 0% { transform: scale(1); } 50% { transform: scale(1.05); } 100% { transform: scale(1); } }
    .voice-status { color: var(--text-secondary); margin-top: 1.5rem; font-size: 0.95rem; }
    .waveform { display: flex; gap: 3px; align-items: center; justify-content: center; height: 40px; margin: 1rem 0; }
    .bar { width: 4px; background: linear-gradient(135deg, #667eea, #764ba2); border-radius: 2px; animation: wave 0.8s ease-in-out infinite alternate; }
    @keyframes wave { 0% { height: 8px; } 100% { height: 35px; } }
    .transcript-box, .result-box { width: 100%; max-width: 500px; background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 16px; padding: 1.2rem; margin-top: 1.5rem; }
    .box-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.8rem; }
    .box-header .material-icons { font-size: 20px; color: var(--primary-color); }
    .box-header h4 { margin: 0; color: var(--text-primary); font-size: 0.9rem; }
    .transcript-text, .result-text { margin: 0; color: var(--text-primary); line-height: 1.7; white-space: pre-wrap; font-size: 0.95rem; }
    .voice-sidebar { display: flex; flex-direction: column; gap: 1.2rem; }
    .sidebar-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 16px; padding: 1.2rem; }
    .card-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem; }
    .card-header .material-icons { font-size: 20px; color: var(--primary-color); }
    .card-header h4 { margin: 0; color: var(--text-primary); font-size: 0.95rem; }
    .manual-input { display: flex; gap: 0.5rem; }
    .manual-input input { flex: 1; padding: 0.8rem 1rem; background: var(--bg-hover); border: 1px solid var(--border-color); border-radius: 10px; color: var(--text-primary); font-size: 0.95rem; transition: border-color 0.2s; }
    .manual-input input:focus { border-color: var(--primary-color); outline: none; }
    .manual-input input::placeholder { color: var(--text-secondary); }
    .send-btn { width: 44px; height: 44px; border-radius: 10px; border: none; background: linear-gradient(135deg, #667eea, #764ba2); color: white; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.2s; flex-shrink: 0; }
    .send-btn:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 15px rgba(102,126,234,0.4); }
    .send-btn:disabled { opacity: 0.4; cursor: not-allowed; }
    .upload-area { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1.5rem; border: 2px dashed var(--border-color); border-radius: 12px; cursor: pointer; transition: all 0.2s; text-align: center; }
    .upload-area:hover { border-color: var(--primary-color); background: rgba(102, 126, 234, 0.05); }
    .upload-area .material-icons { font-size: 36px; color: var(--primary-color); }
    .upload-area span { color: var(--text-secondary); font-size: 0.85rem; }
    .upload-hint { color: var(--text-secondary) !important; font-size: 0.75rem !important; }
    .suggestions { display: flex; flex-direction: column; gap: 0.5rem; }
    .suggestion { display: flex; align-items: center; gap: 0.6rem; padding: 0.7rem 1rem; background: var(--bg-hover); border: 1px solid var(--border-color); border-radius: 10px; color: var(--text-primary); font-size: 0.85rem; cursor: pointer; transition: all 0.2s; text-align: left; width: 100%; }
    .suggestion:hover { border-color: var(--primary-color); background: rgba(102, 126, 234, 0.1); color: var(--text-primary); }
    .suggestion .material-icons { font-size: 18px; color: var(--primary-color); }
    @media (max-width: 900px) {
      .voice-container { grid-template-columns: 1fr; }
      .voice-sidebar { order: 2; }
    }
  `]
})
export class VoiceComponent implements OnDestroy {
  isRecording = signal(false);
  processing = signal(false);
  transcript = signal('');
  result = signal('');
  manualText = '';
  bars = Array.from({ length: 20 }, () => Math.random() * 25 + 8);
  private mediaRecorder: MediaRecorder | null = null;
  private audioChunks: Blob[] = [];
  private barInterval: ReturnType<typeof setInterval> | null = null;
  private subs: Subscription[] = [];

  constructor(private api: ApiService, private toast: ToastService) {}

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    if (this.barInterval) clearInterval(this.barInterval);
    this.mediaRecorder?.stream.getTracks().forEach(t => t.stop());
  }

  getStatusText(): string {
    if (this.isRecording()) return 'Listening... Click to stop';
    if (this.processing()) return 'Processing your voice...';
    return 'Click the microphone to start speaking';
  }

  async toggleRecording(): Promise<void> {
    if (this.isRecording()) {
      this.stopRecording();
    } else {
      await this.startRecording();
    }
  }

  async startRecording(): Promise<void> {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      this.mediaRecorder = new MediaRecorder(stream);
      this.audioChunks = [];
      this.mediaRecorder.ondataavailable = (e) => this.audioChunks.push(e.data);
      this.mediaRecorder.onstop = () => this.processAudio();
      this.mediaRecorder.start();
      this.isRecording.set(true);
      this.barInterval = setInterval(() => {
        this.bars = this.bars.map(() => Math.random() * 30 + 5);
      }, 150);
    } catch {
      this.toast.error('Microphone access denied. Please allow microphone access.');
    }
  }

  stopRecording(): void {
    this.mediaRecorder?.stop();
    this.mediaRecorder?.stream.getTracks().forEach(t => t.stop());
    this.isRecording.set(false);
    clearInterval(this.barInterval);
  }

  processAudio(): void {
    const blob = new Blob(this.audioChunks, { type: 'audio/webm' });
    const file = new File([blob], 'recording.webm', { type: 'audio/webm' });
    this.processing.set(true);
    this.subs.push(this.api.transcribeAudio(file).subscribe({
      next: (r) => { this.transcript.set(r.text); this.processing.set(false); this.sendTranscript(r.text); },
      error: () => { this.toast.error('Transcription failed'); this.processing.set(false); }
    }));
  }

  sendTranscript(text: string): void {
    this.processing.set(true);
    this.subs.push(this.api.sendMessage(text).subscribe({
      next: (r) => { this.result.set(r.result); this.processing.set(false); },
      error: () => { this.toast.error('Failed to process command'); this.processing.set(false); }
    }));
  }

  sendCommand(): void {
    if (!this.manualText.trim()) return;
    this.processing.set(true);
    this.subs.push(this.api.sendMessage(this.manualText).subscribe({
      next: (r) => { this.result.set(r.result); this.manualText = ''; this.processing.set(false); },
      error: () => { this.toast.error('Failed to process command'); this.processing.set(false); }
    }));
  }

  quickCommand(text: string): void {
    this.manualText = text;
    this.sendCommand();
  }

  onAudioUpload(event: any): void {
    const file = event.target.files?.[0];
    if (!file) return;
    this.processing.set(true);
    this.subs.push(this.api.transcribeAudio(file).subscribe({
      next: (r) => { this.transcript.set(r.text); this.processing.set(false); this.sendTranscript(r.text); },
      error: () => { this.toast.error('Transcription failed'); this.processing.set(false); }
    }));
    event.target.value = '';
  }
}
