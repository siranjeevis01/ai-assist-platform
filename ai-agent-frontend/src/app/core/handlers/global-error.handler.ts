import { ErrorHandler, Injectable, NgZone } from '@angular/core';
import { ToastService } from '../../shared/toast/toast.service';

@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  constructor(private toast: ToastService, private zone: NgZone) {}

  handleError(error: any): void {
    console.error('Global error:', error);
    this.zone.run(() => {
      this.toast.error('Something went wrong. Please try again.');
    });
  }
}
