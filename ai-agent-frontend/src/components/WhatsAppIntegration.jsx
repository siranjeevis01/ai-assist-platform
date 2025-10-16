// src/components/WhatsAppIntegration.jsx
import React, { useState, useEffect } from 'react';
import { QrCode, CheckCircle, XCircle, RefreshCw, MessageCircle } from 'lucide-react';
import { integrationService } from '../services/api';

export default function WhatsAppIntegration() {
  const [status, setStatus] = useState(null);
  const [qrCode, setQrCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [connectionStatus, setConnectionStatus] = useState('disconnected');

  useEffect(() => {
    loadStatus();
    const interval = setInterval(loadStatus, 10000); // Check every 10 seconds
    return () => clearInterval(interval);
  }, []);

  const loadStatus = async () => {
    try {
      const statusData = await integrationService.getWhatsAppStatus();
      setStatus(statusData);
      setConnectionStatus(statusData.isConnected ? 'connected' : 'disconnected');
    } catch (error) {
      console.error('Failed to load WhatsApp status:', error);
      setConnectionStatus('error');
    }
  };

  const connectWhatsApp = async () => {
    setLoading(true);
    try {
      await integrationService.connectWhatsApp();
      const qrData = await integrationService.getQRCode();
      setQrCode(qrData.qrCode);
      setConnectionStatus('waiting');
    } catch (error) {
      console.error('Failed to connect WhatsApp:', error);
      setConnectionStatus('error');
    } finally {
      setLoading(false);
    }
  };

  const refreshQRCode = async () => {
    try {
      const qrData = await integrationService.getQRCode();
      setQrCode(qrData.qrCode);
    } catch (error) {
      console.error('Failed to refresh QR code:', error);
    }
  };

  const disconnectWhatsApp = async () => {
    try {
      await integrationService.disconnectWhatsApp();
      setConnectionStatus('disconnected');
      setQrCode('');
      loadStatus();
    } catch (error) {
      console.error('Failed to disconnect WhatsApp:', error);
    }
  };

  const testWhatsApp = async () => {
    try {
      await integrationService.testWhatsApp();
      alert('Test message sent successfully!');
    } catch (error) {
      alert('Failed to send test message');
      console.error('Failed to send test message:', error);
    }
  };

  if (!status && connectionStatus !== 'error') {
    return <div className="loading">Loading WhatsApp status...</div>;
  }

  return (
    <div className="integration-card">
      <div className="integration-header">
        <div className="integration-info">
          <div className="integration-title">
            <MessageCircle size={24} />
            <h3>WhatsApp Integration</h3>
          </div>
          <p>Connect WhatsApp to receive notifications and interact with AI assistant</p>
        </div>
        <div className={`status-indicator ${connectionStatus}`}>
          {connectionStatus === 'connected' && <CheckCircle size={20} />}
          {connectionStatus === 'disconnected' && <XCircle size={20} />}
          {connectionStatus === 'waiting' && <RefreshCw size={20} className="spinning" />}
          {connectionStatus === 'error' && <XCircle size={20} />}
          <span>
            {connectionStatus === 'connected' && 'Connected'}
            {connectionStatus === 'disconnected' && 'Disconnected'}
            {connectionStatus === 'waiting' && 'Waiting for QR Scan'}
            {connectionStatus === 'error' && 'Connection Error'}
          </span>
        </div>
      </div>

      {connectionStatus === 'disconnected' && (
        <div className="integration-actions">
          {qrCode ? (
            <div className="qr-section">
              <div className="qr-header">
                <QrCode size={24} />
                <h4>Scan QR Code</h4>
              </div>
              <div className="qr-container">
                <img src={qrCode} alt="WhatsApp QR Code" className="qr-code" />
              </div>
              <p>Open WhatsApp → Menu → Linked Devices → Link a Device → Scan QR Code</p>
              <div className="qr-actions">
                <button onClick={refreshQRCode} className="btn btn-secondary">
                  <RefreshCw size={16} />
                  Refresh QR Code
                </button>
                <button onClick={disconnectWhatsApp} className="btn btn-accent">
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <button 
              onClick={connectWhatsApp} 
              className="btn btn-primary"
              disabled={loading}
            >
              {loading ? (
                <>
                  <RefreshCw size={16} className="spinning" />
                  Initializing...
                </>
              ) : (
                <>
                  <MessageCircle size={16} />
                  Connect WhatsApp
                </>
              )}
            </button>
          )}
        </div>
      )}

      {connectionStatus === 'waiting' && (
        <div className="integration-actions">
          <div className="qr-section">
            <div className="qr-header">
              <QrCode size={24} />
              <h4>Scan QR Code to Connect</h4>
            </div>
            <div className="qr-container">
              <img src={qrCode} alt="WhatsApp QR Code" className="qr-code" />
            </div>
            <p>Waiting for you to scan the QR code with WhatsApp...</p>
            <div className="qr-actions">
              <button onClick={refreshQRCode} className="btn btn-secondary">
                <RefreshCw size={16} />
                Refresh QR Code
              </button>
              <button onClick={disconnectWhatsApp} className="btn btn-accent">
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {connectionStatus === 'connected' && (
        <div className="connected-info">
          <div className="success-message">
            <CheckCircle size={20} />
            <span>WhatsApp is connected and ready</span>
          </div>
          <div className="connected-actions">
            <button 
              onClick={testWhatsApp}
              className="btn btn-secondary"
            >
              <MessageCircle size={16} />
              Send Test Message
            </button>
            <button 
              onClick={disconnectWhatsApp}
              className="btn btn-accent"
            >
              Disconnect
            </button>
          </div>
          <div className="connection-details">
            <p><strong>Last Sync:</strong> {status?.lastSync ? new Date(status.lastSync).toLocaleString() : 'Just now'}</p>
            <p><strong>Messages Processed:</strong> {status?.messagesProcessed || 0}</p>
          </div>
        </div>
      )}

      {connectionStatus === 'error' && (
        <div className="error-info">
          <div className="error-message">
            <XCircle size={20} />
            <span>Failed to connect to WhatsApp</span>
          </div>
          <button 
            onClick={connectWhatsApp} 
            className="btn btn-primary"
          >
            <RefreshCw size={16} />
            Retry Connection
          </button>
        </div>
      )}
    </div>
  );
}