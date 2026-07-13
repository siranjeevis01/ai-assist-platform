import { Component, OnInit, signal, ElementRef, ViewChild, AfterViewChecked, OnDestroy } from '@angular/core';
import { NgIf, NgFor, NgClass, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../shared/toast/toast.service';

interface ChatDisplay {
  text: string;
  sender: 'user' | 'bot';
  timestamp: Date;
}

@Component({
  selector: 'app-messages',
  standalone: true,
  imports: [NgIf, NgFor, NgClass, DatePipe, FormsModule],
  template: `
    <div class="messages-page">
      <div class="page-header">
        <div class="header-content">
          <h1>AI Assistant</h1>
          <p>Chat with your AI assistant</p>
        </div>
        <button *ngIf="messages().length > 0" class="btn btn-secondary" (click)="clearHistory()">
          <span class="material-icons">delete_sweep</span> Clear Chat
        </button>
      </div>

      <div class="chat-container">
        <div class="chat-messages" #messagesContainer>
          <div *ngIf="messages().length === 0" class="welcome-message">
            <span class="material-icons" style="font-size:48px">smart_toy</span>
            <h3>Hello! I'm your AI Assistant</h3>
            <p>I can help you with:</p>
            <ul>
              <li>Scheduling meetings and events</li>
              <li>Creating and managing tasks</li>
              <li>Setting reminders</li>
              <li>Checking and sending emails</li>
            </ul>
            <p>Try one of these commands or ask me anything!</p>
            <div class="quick-commands">
              <button *ngFor="let cmd of quickCommands" class="quick-command" (click)="newMessage = cmd">{{ cmd }}</button>
            </div>
          </div>

          <div *ngFor="let msg of messages(); let i = index" class="message" [ngClass]="msg.sender">
            <div class="message-avatar">
              <span class="material-icons">{{ msg.sender === 'bot' ? 'smart_toy' : 'person' }}</span>
            </div>
            <div class="message-content">
              <div class="message-text">{{ msg.text }}</div>
              <div class="message-time">{{ msg.timestamp | date:'shortTime' }}</div>
            </div>
          </div>

          <div *ngIf="loading()" class="message bot">
            <div class="message-avatar"><span class="material-icons">smart_toy</span></div>
            <div class="message-content">
              <div class="typing-indicator"><span></span><span></span><span></span></div>
            </div>
          </div>
          <div #scrollTarget></div>
        </div>

        <form (ngSubmit)="sendMessage()" class="chat-input">
          <input type="text" [(ngModel)]="newMessage" name="message" placeholder="Type your message..." [disabled]="loading()" />
          <button type="submit" [disabled]="loading() || !newMessage.trim()">
            <span class="material-icons">send</span>
          </button>
        </form>
      </div>
    </div>
  `,
  styleUrl: './messages.component.scss'
})
export class MessagesComponent implements OnInit, AfterViewChecked, OnDestroy {
  @ViewChild('scrollTarget') scrollTarget!: ElementRef;
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;

  messages = signal<ChatDisplay[]>([]);
  newMessage = '';
  loading = signal(false);
  private subs: Subscription[] = [];

  quickCommands = [
    'Schedule meeting tomorrow at 3 PM',
    'Create task for project report',
    'Show my calendar',
    'Check my emails'
  ];

  constructor(
    private api: ApiService,
    private toast: ToastService
  ) {}

  ngOnInit(): void { this.loadHistory(); }
  ngOnDestroy(): void { this.subs.forEach(s => s.unsubscribe()); }

  ngAfterViewChecked(): void { this.scrollToBottom(); }

  private scrollToBottom(): void {
    try { this.scrollTarget?.nativeElement?.scrollIntoView({ behavior: 'smooth' }); } catch {}
  }

  loadHistory(): void {
    this.subs.push(this.api.getChatHistory().subscribe({
      next: msgs => {
        this.messages.set(msgs.map(m => ({
          text: m.text,
          sender: m.role === 'assistant' ? 'bot' : 'user',
          timestamp: new Date(m.createdAt)
        })));
      },
      error: () => this.toast.error('Failed to load chat history')
    }));
  }

  sendMessage(): void {
    const text = this.newMessage.trim();
    if (!text) return;

    this.messages.update(msgs => [...msgs, { text, sender: 'user', timestamp: new Date() }]);
    this.newMessage = '';
    this.loading.set(true);

    this.subs.push(this.api.sendMessage(text).subscribe({
      next: (res) => {
        this.messages.update(msgs => [...msgs, { text: res.result || 'I received your message!', sender: 'bot', timestamp: new Date() }]);
        this.loading.set(false);
      },
      error: () => {
        this.messages.update(msgs => [...msgs, { text: 'Sorry, I encountered an error. Please try again.', sender: 'bot', timestamp: new Date() }]);
        this.loading.set(false);
      }
    }));
  }

  clearHistory(): void {
    this.subs.push(this.api.clearChatHistory().subscribe(() => {
      this.messages.set([]);
      this.toast.success('Chat history cleared');
    }));
  }
}
