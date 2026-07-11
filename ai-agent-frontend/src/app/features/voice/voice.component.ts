import { Component, signal } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Component({
  selector: 'app-voice',
  standalone: true,
  imports: [NgIf, FormsModule],
  template: `
    <div class="page-container">
      <div class="page-header">
        <div class="header-content">
          <h1>Voice Assistant</h1>
          <p>Speak to your AI agent using your microphone</p>
        </div>
      </div>

      <div class="voice-area">
        <div class="voice-visual" [class.recording]="isRecording()">
          <button class="mic-btn" (click)="toggleRecording()" [class.active]="isRecording()">
            <span class="material-icons">{{ isRecording() ? 'stop' : 'mic' }}</span>
          </button>
          <p class="voice-status">{{ getStatusText() }}</p>
        </div>

        <div class="transcript-box" *ngIf="transcript()">
          <h4>Last Transcription:</h4>
          <p class="transcript-text">{{ transcript() }}</p>
        </div>

        <div class="result-box" *ngIf="result()">
          <h4>AI Response:</h4>
          <p class="result-text">{{ result() }}</p>
        </div>

        <div class="manual-input">
          <h4>Or type a command:</h4>
          <div class="input-row">
            <input type="text" [(ngModel)]="manualText" placeholder="Type a message for the AI agent..." (keyup.enter)="sendCommand()" />
            <button class="btn btn-primary" (click)="sendCommand()" [disabled]="processing()">
              <span class="material-icons">send</span>
            </button>
          </div>
        </div>

        <div class="upload-section">
          <h4>Upload audio file:</h4>
          <label class="btn btn-secondary upload-label">
            <span class="material-icons">audio_file</span> Choose Audio
            <input type="file" (change)="onAudioUpload($event)" accept="audio/*" hidden />
          </label>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .voice-area { max-width: 600px; margin: 0 auto; text-align: center; }
    .voice-visual { padding: 3rem 2rem; }
    .voice-visual.recording .mic-btn { animation: pulse 1.5s infinite; }
    @keyframes pulse { 0%, 100% { box-shadow: 0 0 0 0 rgba(102, 126, 234, 0.4); } 50% { box-shadow: 0 0 0 20px rgba(102, 126, 234, 0); } }
    .mic-btn { width: 100px; height: 100px; border-radius: 50%; border: none; background: linear-gradient(135deg, #667eea, #764ba2); color: white; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.3s; }
    .mic-btn .material-icons { font-size: 40px; }
    .mic-btn:hover { transform: scale(1.05); }
    .mic-btn.active { background: linear-gradient(135deg, #f5576c, #ff6b6b); }
    .voice-status { color: #888; margin-top: 1rem; }
    .transcript-box, .result-box { background: #1e1e2e; border: 1px solid #333; border-radius: 12px; padding: 1.2rem; margin: 1rem 0; text-align: left; }
    .transcript-box h4, .result-box h4, .manual-input h4, .upload-section h4 { margin: 0 0 0.8rem; color: #888; font-size: 0.85rem; text-transform: uppercase; letter-spacing: 0.5px; }
    .transcript-text, .result-text { margin: 0; color: #e0e0e0; line-height: 1.6; white-space: pre-wrap; }
    .input-row { display: flex; gap: 0.5rem; }
    .input-row input { flex: 1; padding: 0.8rem 1rem; background: #2a2a3e; border: 1px solid #444; border-radius: 8px; color: #e0e0e0; font-size: 1rem; }
    .upload-section { margin-top: 2rem; }
    .upload-label { cursor: pointer; display: inline-flex; align-items: center; gap: 0.5rem; }
  `]
})
export class VoiceComponent {
  isRecording = signal(false);
  processing = signal(false);
  transcript = signal('');
  result = signal('');
  manualText = '';
  private mediaRecorder: MediaRecorder | null = null;
  private audioChunks: Blob[] = [];

  constructor(private api: ApiService, private toast: ToastService) {}

  getStatusText(): string {
    if (this.isRecording()) return 'Recording... Click to stop';
    if (this.processing()) return 'Processing...';
    return 'Click the microphone to start';
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
    } catch {
      this.toast.error('Microphone access denied. Please allow microphone access.');
    }
  }

  stopRecording(): void {
    this.mediaRecorder?.stop();
    this.mediaRecorder?.stream.getTracks().forEach(t => t.stop());
    this.isRecording.set(false);
  }

  processAudio(): void {
    const blob = new Blob(this.audioChunks, { type: 'audio/webm' });
    const file = new File([blob], 'recording.webm', { type: 'audio/webm' });
    this.processing.set(true);
    this.api.transcribeAudio(file).subscribe({
      next: (r) => { this.transcript.set(r.text); this.processing.set(false); this.sendTranscript(r.text); },
      error: () => { this.toast.error('Transcription failed'); this.processing.set(false); }
    });
  }

  sendTranscript(text: string): void {
    this.processing.set(true);
    this.api.sendMessage(text).subscribe({
      next: (r) => { this.result.set(r.result); this.processing.set(false); },
      error: () => { this.toast.error('Failed to process command'); this.processing.set(false); }
    });
  }

  sendCommand(): void {
    if (!this.manualText.trim()) return;
    this.processing.set(true);
    this.api.sendMessage(this.manualText).subscribe({
      next: (r) => { this.result.set(r.result); this.manualText = ''; this.processing.set(false); },
      error: () => { this.toast.error('Failed to process command'); this.processing.set(false); }
    });
  }

  onAudioUpload(event: any): void {
    const file = event.target.files?.[0];
    if (!file) return;
    this.processing.set(true);
    this.api.transcribeAudio(file).subscribe({
      next: (r) => { this.transcript.set(r.text); this.processing.set(false); this.sendTranscript(r.text); },
      error: () => { this.toast.error('Transcription failed'); this.processing.set(false); }
    });
    event.target.value = '';
  }
}
