import { Component } from '@angular/core';
import { NgFor } from '@angular/common';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  imports: [NgFor],
  template: `
    <div class="toast-container">
      <div *ngFor="let toast of toastService.toasts()" class="toast" [class]="toast.type" (click)="toastService.dismiss(toast.id)">
        <span class="toast-icon">
          {{ toast.type === 'success' ? '✓' : toast.type === 'error' ? '✕' : toast.type === 'warning' ? '⚠' : 'ℹ' }}
        </span>
        <span class="toast-message">{{ toast.message }}</span>
      </div>
    </div>
  `,
  styleUrl: './toast.component.scss'
})
export class ToastComponent {
  constructor(public toastService: ToastService) {}
}
