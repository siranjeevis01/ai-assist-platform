import { Injectable, inject } from '@angular/core';
import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, onMessage, Messaging } from 'firebase/messaging';
import { environment } from '../../../environments/environment';
import { ApiService } from './api.service';
import { ToastService } from '../../shared/toast/toast.service';

@Injectable({ providedIn: 'root' })
export class PushNotificationService {
  private messaging: Messaging | null = null;
  private api = inject(ApiService);
  private toast = inject(ToastService);

  async initialize(): Promise<void> {
    if (!('Notification' in window)) return;
    if (Notification.permission !== 'granted') return;

    try {
      const app = initializeApp(environment.firebase);
      this.messaging = getMessaging(app);

      onMessage(this.messaging, (payload) => {
        const title = payload.notification?.title || 'AI Agent';
        const body = payload.notification?.body || '';
        this.toast.info(`${title}: ${body}`);
      });

      await this.registerToken();
    } catch (err) {
      console.warn('Firebase messaging not available:', err);
    }
  }

  async requestPermission(): Promise<boolean> {
    if (!('Notification' in window)) return false;
    const permission = await Notification.requestPermission();
    if (permission === 'granted') {
      await this.registerToken();
      return true;
    }
    return false;
  }

  async registerToken(): Promise<void> {
    if (!this.messaging) return;
    try {
      const token = await getToken(this.messaging, { vapidKey: 'YOUR_VAPID_KEY' });
      if (token) {
        this.api.registerDeviceToken(token, 'web').subscribe();
      }
    } catch (err) {
      console.warn('Failed to get FCM token:', err);
    }
  }
}
