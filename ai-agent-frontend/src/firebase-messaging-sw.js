importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: 'AIzaSyAzfdBhjVCHoF_BKnq7T4W8OTLDsJ7FdoQ',
  authDomain: 'aiagent-siranjeevis01.firebaseapp.com',
  projectId: 'aiagent-siranjeevis01',
  storageBucket: 'aiagent-siranjeevis01.firebasestorage.app',
  messagingSenderId: '1031107425058',
  appId: '1:1031107425058:web:e50a99ea83ead474976b7b',
});

const messaging = firebase.messaging();

messaging.onBackgroundMessage((payload) => {
  const notificationTitle = payload.notification?.title || 'AI Agent';
  const notificationOptions = {
    body: payload.notification?.body || '',
    icon: '/assets/icons/icon-192x192.png',
    data: payload.data,
  };
  self.registration.showNotification(notificationTitle, notificationOptions);
});
