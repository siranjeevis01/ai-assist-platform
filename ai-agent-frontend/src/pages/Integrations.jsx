// src/pages/Integrations.jsx
import React, { useState, useEffect } from 'react';
import { CheckCircle, XCircle, RefreshCw } from 'lucide-react';
import { integrationService } from '../services/api';

export default function Integrations() {
  const [integrations, setIntegrations] = useState({
    google: { connected: false, loading: false },
    whatsapp: { connected: false, loading: false, qrCode: '' }
  });

  useEffect(() => {
    loadIntegrationStatus();
  }, []);

  const loadIntegrationStatus = async () => {
    try {
      const [googleStatus, whatsappStatus] = await Promise.all([
        integrationService.getGoogleStatus(),
        integrationService.getWhatsAppStatus()
      ]);

      let qrCode = '';
      if (whatsappStatus.qrAvailable && !whatsappStatus.isConnected) {
        qrCode = await loadQRCode();
      }

      setIntegrations(prev => ({
        ...prev,
        google: { ...prev.google, connected: googleStatus.connected || false },
        whatsapp: { 
          ...prev.whatsapp, 
          connected: whatsappStatus.isConnected || false,
          qrCode: qrCode
        }
      }));
    } catch (error) {
      console.error('Failed to load integration status:', error);
    }
  };

  const loadQRCode = async () => {
    try {
      const qrData = await integrationService.getQRCode();
      return qrData.qrCode;
    } catch (error) {
      console.error('Failed to load QR code:', error);
      return '';
    }
  };

  const connectGoogle = async () => {
    setIntegrations(prev => ({ ...prev, google: { ...prev.google, loading: true } }));
    try {
      await integrationService.connectGoogle();
      setTimeout(() => {
        setIntegrations(prev => ({ ...prev, google: { ...prev.google, connected: true, loading: false } }));
      }, 2000);
    } catch (error) {
      console.error('Failed to connect Google:', error);
      setIntegrations(prev => ({ ...prev, google: { ...prev.google, loading: false } }));
    }
  };

  const connectWhatsApp = async () => {
    setIntegrations(prev => ({ ...prev, whatsapp: { ...prev.whatsapp, loading: true } }));
    try {
      await integrationService.connectWhatsApp();
      const qrData = await integrationService.getQRCode();
      setIntegrations(prev => ({
        ...prev,
        whatsapp: { 
          ...prev.whatsapp, 
          qrCode: qrData.qrCode,
          loading: false
        }
      }));
    } catch (error) {
      console.error('Failed to connect WhatsApp:', error);
      setIntegrations(prev => ({ ...prev, whatsapp: { ...prev.whatsapp, loading: false } }));
    }
  };

  const refreshQRCode = async () => {
    try {
      const qrData = await integrationService.getQRCode();
      setIntegrations(prev => ({
        ...prev,
        whatsapp: { 
          ...prev.whatsapp, 
          qrCode: qrData.qrCode
        }
      }));
    } catch (error) {
      console.error('Failed to refresh QR code:', error);
    }
  };

  const IntegrationCard = ({ name, connected, loading, qrCode, onConnect }) => {
    const getIntegrationInfo = (name) => {
      switch (name.toLowerCase()) {
        case 'google':
          return { icon: '🔗', description: 'Calendar & Gmail integration', color: 'var(--primary-gradient)' };
        case 'whatsapp':
          return { icon: '📱', description: 'Messaging integration', color: 'var(--success-gradient)' };
        default:
          return { icon: '⚙️', description: '', color: '#666' };
      }
    };

    const info = getIntegrationInfo(name);

    return (
      <div className="integration-card">
        <div className="integration-header">
          <div className="integration-icon" style={{ background: info.color }}>
            {info.icon}
          </div>
          <div className="integration-info">
            <h3>{name}</h3>
            <p>{info.description}</p>
          </div>
          <div className={`status ${connected ? 'connected' : 'disconnected'}`}>
            {connected ? <CheckCircle size={20} /> : <XCircle size={20} />}
            {connected ? 'Connected' : 'Disconnected'}
          </div>
        </div>

        <div className="integration-actions">
          {connected ? (
            <button className="btn btn-secondary" disabled>Connected</button>
          ) : (
            <button onClick={onConnect} className="btn btn-primary" disabled={loading}>
              {loading ? 'Connecting...' : 'Connect'}
            </button>
          )}
        </div>

        {qrCode && !connected && (
          <div className="qr-section">
            <div className="qr-code">
              <div style={{ padding: '20px', background: 'white', border: '1px solid #ddd', borderRadius: '8px', textAlign: 'center', fontFamily: 'monospace', fontSize: '12px', wordBreak: 'break-all' }}>
                QR Code: {qrCode}
              </div>
            </div>
            <p>Scan this QR code with {name} to connect</p>
            <button onClick={refreshQRCode} className="btn btn-secondary">
              <RefreshCw size={16} /> Refresh QR Code
            </button>
          </div>
        )}
      </div>
    );
  };

  return (
    <div className="integrations-page">
      <div className="page-header">
        <div className="header-content">
          <h1>Integrations</h1>
          <p>Connect your services to the AI Agent</p>
        </div>
        <button onClick={loadIntegrationStatus} className="btn btn-secondary">
          <RefreshCw size={20} /> Refresh Status
        </button>
      </div>

      <div className="integrations-grid">
        <IntegrationCard
          name="Google"
          connected={integrations.google.connected}
          loading={integrations.google.loading}
          onConnect={connectGoogle}
        />

        <IntegrationCard
          name="WhatsApp"
          connected={integrations.whatsapp.connected}
          loading={integrations.whatsapp.loading}
          qrCode={integrations.whatsapp.qrCode}
          onConnect={connectWhatsApp}
        />
      </div>
    </div>
  );
}