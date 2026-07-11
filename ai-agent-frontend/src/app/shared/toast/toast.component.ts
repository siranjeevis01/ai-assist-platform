import { Component } from '@angular/core';
import { NgFor, NgIf } from '@angular/common';
import { Toast, ToastService } from './toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  imports: [NgFor, NgIf],
  template: `
    <div class="toast-container">
      <div *ngFor="let toast of toastService.toasts()" class="toast" [class]="toast.type" (click)="toastService.dismiss(toast.id)">
        <span class="toast-icon">
          {{ toast.type === 'success' ? '✓' : toast.type === 'error' ? '✕' : toast.type === 'warning' ? '⚠' : 'ℹ' }}
        </span>
        <span class="toast-message">{{ toast.message }}</span>
        <button
          *ngIf="toast.undoCallback"
          class="toast-undo-btn"
          (click)="onUndo($event, toast)"
        >Undo</button>
      </div>
    </div>
  `,
  styleUrl: './toast.component.scss'
})
export class ToastComponent {
  constructor(public toastService: ToastService) {}

  onUndo(event: MouseEvent, toast: Toast): void {
    event.stopPropagation();
    toast.undoCallback?.();
    this.toastService.dismiss(toast.id);
  }
}
