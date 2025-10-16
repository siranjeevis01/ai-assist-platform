using AiAgentBackend.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AiAgentBackend.Services.Orchestration;
using System.Net.Sockets;
using System.Net;

namespace AiAgentBackend.Services.Integrations
{
    public class RealWhatsAppWebService : IWhatsAppService, IDisposable
    {
        private readonly WhatsAppOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RealWhatsAppWebService> _logger;
        private IWebDriver? _driver;
        private WebDriverWait? _wait;
        private ChromeDriverService? _chromeDriverService;
        
        private bool _isConnected = false;
        private bool _isInitializing = false;
        private string? _qrData;
        private DateTime? _qrGeneratedAt;
        private CancellationTokenSource? _monitoringCts;
        private readonly object _driverLock = new object();
        private bool _disposed = false;
        private Task? _monitoringTask;

        public event Func<string, string, string, Task>? OnMessageReceived;

        public RealWhatsAppWebService(
            IOptions<WhatsAppOptions> options,
            IServiceScopeFactory scopeFactory,
            ILogger<RealWhatsAppWebService> logger)
        {
            _options = options.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private void InitializeDriver()
        {
            try
            {
                lock (_driverLock)
                {
                    if (_disposed) return;

                    // Clean up any existing driver first
                    CleanupDriver();

                    var chromeOptions = new ChromeOptions();
                    ConfigureChromeOptions(chromeOptions);
                    
                    // Create driver service with better configuration
                    _chromeDriverService = ChromeDriverService.CreateDefaultService();
                    _chromeDriverService.HideCommandPromptWindow = true;
                    _chromeDriverService.Port = FindFreePort();
                    
                    // Configure driver with better timeouts
                    _driver = new ChromeDriver(_chromeDriverService, chromeOptions, TimeSpan.FromSeconds(120));
                    _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
                    
                    _logger.LogInformation("Real WhatsApp Web service initialized successfully on port {Port}", _chromeDriverService.Port);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ChromeDriver");
                throw;
            }
        }

        private int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private void CleanupDriver()
        {
            try
            {
                _monitoringCts?.Cancel();
                _monitoringTask = null;

                if (_driver != null)
                {
                    try 
                    { 
                        _driver.Quit(); 
                    } 
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error quitting driver during cleanup");
                    }
                    try 
                    { 
                        _driver.Dispose(); 
                    } 
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error disposing driver during cleanup");
                    }
                    _driver = null;
                }

                if (_chromeDriverService != null)
                {
                    try 
                    { 
                        _chromeDriverService.Dispose(); 
                    } 
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error disposing driver service during cleanup");
                    }
                    _chromeDriverService = null;
                }

                _wait = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during driver cleanup");
            }
        }

        private void ConfigureChromeOptions(ChromeOptions options)
        {
            // Enhanced Chrome options for better stability
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-plugins");
            options.AddArgument("--disable-images");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-software-rasterizer");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--no-first-run");
            options.AddArgument("--disable-default-apps");
            options.AddArgument("--disable-features=VizDisplayCompositor");
            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-renderer-backgrounding");
            options.AddArgument("--disable-component-extensions-with-background-pages");
            
            // User agent and window size
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            options.AddArgument("--window-size=1920,1080");
            
            // Exclude automation detection
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            
            // Performance options
            options.PageLoadStrategy = PageLoadStrategy.Eager;
            options.UnhandledPromptBehavior = UnhandledPromptBehavior.Ignore;
            
            // Logging
            options.SetLoggingPreference(LogType.Browser, OpenQA.Selenium.LogLevel.Off);
            options.SetLoggingPreference(LogType.Driver, OpenQA.Selenium.LogLevel.Off);
        }

        public Task<WhatsAppStatus> GetStatusAsync()
        {
            return Task.FromResult(new WhatsAppStatus
            {
                IsConnected = _isConnected,
                Status = _isConnected ? "connected" : (_isInitializing ? "initializing" : "disconnected"),
                QrAvailable = !string.IsNullOrEmpty(_qrData) && !_isConnected,
                Timestamp = DateTime.UtcNow,
                IsInitializing = _isInitializing,
                QrGeneratedAt = _qrGeneratedAt
            });
        }

        public Task<QrResponse> GetQrCodeAsync()
        {
            try
            {
                if (_isConnected)
                {
                    return Task.FromResult(new QrResponse
                    {
                        QrCode = string.Empty,
                        QrImage = string.Empty,
                        Message = "WhatsApp is already connected",
                        Status = "connected",
                        ExpiresAt = null
                    });
                }

                if (string.IsNullOrEmpty(_qrData))
                {
                    return Task.FromResult(new QrResponse
                    {
                        QrCode = string.Empty,
                        QrImage = string.Empty,
                        Message = "No QR code available. Please initialize connection first.",
                        Status = "not_available",
                        ExpiresAt = null
                    });
                }

                return Task.FromResult(new QrResponse
                {
                    QrCode = _qrData,
                    QrImage = string.Empty,
                    Message = "Scan the QR code in the WhatsApp Web interface",
                    Status = "qr_available",
                    ExpiresAt = _qrGeneratedAt?.AddMinutes(20)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting QR code");
                return Task.FromResult(new QrResponse
                {
                    QrCode = string.Empty,
                    QrImage = string.Empty,
                    Message = "Error retrieving QR code",
                    Status = "error",
                    ExpiresAt = null
                });
            }
        }

        public async Task<InitializeConnectionResult> InitializeConnectionAsync()
        {
            if (_isInitializing)
            {
                return new InitializeConnectionResult
                {
                    Success = false,
                    Message = "WhatsApp connection is already initializing...",
                    QrCode = _qrData
                };
            }

            _isInitializing = true;
            
            try
            {
                _logger.LogInformation("Initializing real WhatsApp Web connection...");

                // Ensure driver is ready
                if (_driver == null)
                {
                    InitializeDriver();
                }

                // Navigate to WhatsApp Web with retry logic
                await NavigateToWhatsAppWithRetry();
                
                // Wait for QR code to load with better detection
                await WaitForQrCodeAsync();
                
                // Start monitoring connection status
                _monitoringCts = new CancellationTokenSource();
                _monitoringTask = Task.Run(async () => await MonitorConnectionAsync(_monitoringCts.Token));

                return new InitializeConnectionResult
                {
                    Success = true,
                    Message = "WhatsApp Web opened successfully. Scan the QR code in the browser window.",
                    QrCode = "real_whatsapp_web",
                    NextStep = "Scan the QR code displayed in the Chrome browser window"
                };
            }
            catch (Exception ex)
            {
                _isInitializing = false;
                _logger.LogError(ex, "Failed to initialize WhatsApp Web connection");
                return new InitializeConnectionResult
                {
                    Success = false,
                    Message = $"Failed to initialize connection: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        private async Task NavigateToWhatsAppWithRetry()
        {
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (_driver == null) throw new InvalidOperationException("Driver is not initialized");
                    
                    _logger.LogInformation("Navigating to WhatsApp Web... Attempt {Attempt}", i + 1);
                    _driver.Navigate().GoToUrl("https://web.whatsapp.com");
                    
                    // Wait for page to load with better detection
                    await WaitForPageLoad();
                    
                    // Check if we're on WhatsApp Web
                    if (_driver.Url.Contains("web.whatsapp.com"))
                    {
                        _logger.LogInformation("Successfully navigated to WhatsApp Web");
                        return;
                    }
                    
                    _logger.LogWarning("Navigation attempt {Attempt} failed, retrying...", i + 1);
                    await Task.Delay(3000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Navigation attempt {Attempt} failed", i + 1);
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(3000);
                }
            }
            
            throw new Exception("Failed to navigate to WhatsApp Web after multiple attempts");
        }

        private async Task WaitForPageLoad()
        {
            if (_driver == null) return;

            var maxWait = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWait)
            {
                try
                {
                    // FIXED: Use IJavaScriptExecutor instead of ExecuteScript directly on IWebDriver
                    var jsExecutor = (IJavaScriptExecutor)_driver;
                    var readyState = jsExecutor.ExecuteScript("return document.readyState") as string;
                    if (readyState == "complete")
                    {
                        _logger.LogInformation("Page loaded successfully");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Page not ready yet: {Error}", ex.Message);
                }
                await Task.Delay(1000);
            }
            throw new Exception("Page load timeout");
        }

        private async Task WaitForQrCodeAsync()
        {
            try
            {
                var maxWaitTime = TimeSpan.FromSeconds(45);
                var startTime = DateTime.UtcNow;

                _logger.LogInformation("Waiting for QR code to appear...");

                while (DateTime.UtcNow - startTime < maxWaitTime)
                {
                    try
                    {
                        if (_driver == null) throw new InvalidOperationException("WebDriver is not initialized");

                        // Multiple strategies to detect QR code
                        var qrElement = FindQrElement();
                        if (qrElement != null && qrElement.Displayed)
                        {
                            _qrData = $"whatsapp_web_qr_{DateTime.UtcNow.Ticks}";
                            _qrGeneratedAt = DateTime.UtcNow;
                            _logger.LogInformation("✅ QR code detected and ready for scanning");
                            return;
                        }

                        // Check if we're already connected (QR might not appear if already logged in)
                        if (await IsConnectedToWhatsAppAsync())
                        {
                            await HandleSuccessfulConnectionAsync();
                            return;
                        }

                        _logger.LogDebug("QR code not found yet, continuing to wait...");
                        await Task.Delay(2000);
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogDebug("QR code not found yet, continuing to wait...");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Error while waiting for QR: {Error}", ex.Message);
                    }
                }

                throw new Exception("QR code not found within timeout period");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for QR code");
                throw;
            }
        }

        private IWebElement? FindQrElement()
        {
            if (_driver == null) return null;

            try
            {
                // Strategy 1: Look for canvas element (most common)
                var canvasElements = _driver.FindElements(By.TagName("canvas"));
                foreach (var canvas in canvasElements)
                {
                    if (canvas.Displayed && canvas.Size.Width > 200 && canvas.Size.Height > 200)
                    {
                        return canvas;
                    }
                }

                // Strategy 2: Look for QR code container
                var qrContainers = _driver.FindElements(By.CssSelector("[data-ref]"));
                foreach (var container in qrContainers)
                {
                    if (container.Displayed && container.GetAttribute("data-ref")?.Contains("QR") == true)
                    {
                        return container;
                    }
                }

                // Strategy 3: Look for specific WhatsApp QR classes
                var qrSelectors = new[]
                {
                    "div[class*='qrcode']",
                    "div[class*='qr']",
                    "canvas[class*='qrcode']",
                    "canvas[class*='qr']"
                };

                foreach (var selector in qrSelectors)
                {
                    try
                    {
                        var element = _driver.FindElement(By.CssSelector(selector));
                        if (element.Displayed) return element;
                    }
                    catch (NoSuchElementException) { }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error finding QR element: {Error}", ex.Message);
                return null;
            }
        }

        private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Monitoring WhatsApp Web connection...");

                var maxWaitTime = TimeSpan.FromMinutes(15);
                var checkInterval = TimeSpan.FromSeconds(3);
                var startTime = DateTime.UtcNow;

                while (!cancellationToken.IsCancellationRequested && 
                       !_disposed &&
                       DateTime.UtcNow - startTime < maxWaitTime)
                {
                    try
                    {
                        if (await IsConnectedToWhatsAppAsync())
                        {
                            await HandleSuccessfulConnectionAsync();
                            return;
                        }

                        // Check if QR code is still present
                        if (await IsQrCodeStillPresentAsync())
                        {
                            _logger.LogDebug("Still waiting for QR scan...");
                        }
                        else if (!_isConnected)
                        {
                            _logger.LogInformation("QR code disappeared, checking if connected...");
                            if (await IsConnectedToWhatsAppAsync())
                            {
                                await HandleSuccessfulConnectionAsync();
                                return;
                            }
                            else
                            {
                                _logger.LogWarning("QR code disappeared but connection not established");
                                // Reinitialize QR detection
                                await WaitForQrCodeAsync();
                            }
                        }

                        await Task.Delay(checkInterval, cancellationToken);
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("WebDriver was disposed during monitoring");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during connection monitoring");
                        if (_disposed) break;
                        await Task.Delay(checkInterval, cancellationToken);
                    }
                }

                if (!_isConnected && !_disposed)
                {
                    await HandleConnectionTimeoutAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection monitoring failed");
                if (!_disposed)
                {
                    await HandleConnectionFailureAsync();
                }
            }
        }

        private async Task<bool> IsConnectedToWhatsAppAsync()
        {
            try
            {
                if (_driver == null || _disposed) return false;

                return await Task.Run(() =>
                {
                    try
                    {
                        if (_driver == null) return false;
                        
                        var currentUrl = _driver.Url;
                        if (string.IsNullOrEmpty(currentUrl) || !currentUrl.Contains("web.whatsapp.com"))
                            return false;

                        // Enhanced connection detection
                        var connectionIndicators = new[]
                        {
                            "[data-testid='side']", // Side panel
                            "[data-testid='main']", // Main panel
                            "[data-testid='chat-list']", // Chat list
                            "[data-testid='conversation-panel']", // Conversation panel
                            "div[class*='two']", // Two-pane layout
                            "div[class*='three']" // Three-pane layout
                        };

                        foreach (var selector in connectionIndicators)
                        {
                            try
                            {
                                var element = _driver.FindElement(By.CssSelector(selector));
                                if (element.Displayed)
                                {
                                    if (!_isConnected)
                                    {
                                        _logger.LogInformation("✅ WhatsApp Web connection detected via: {Selector}", selector);
                                    }
                                    return true;
                                }
                            }
                            catch (NoSuchElementException) { }
                        }

                        return false;
                    }
                    catch (WebDriverException ex)
                    {
                        _logger.LogWarning(ex, "WebDriver exception while checking connection");
                        return false;
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogDebug("Driver was disposed during connection check");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error checking WhatsApp connection status");
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsQrCodeStillPresentAsync()
        {
            try
            {
                if (_driver == null || _disposed) return false;

                return await Task.Run(() =>
                {
                    try
                    {
                        if (_driver == null) return false;
                        
                        var qrElement = FindQrElement();
                        return qrElement != null && qrElement.Displayed;
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        private async Task HandleSuccessfulConnectionAsync()
        {
            try
            {
                _isConnected = true;
                _isInitializing = false;
                _qrData = null;
                _qrGeneratedAt = null;

                // Update database status
                await UpdateConnectionStatusInDatabase(true);
                
                // Start message monitoring
                _ = Task.Run(async () => await MonitorIncomingMessagesAsync());
                
                // Send welcome messages
                await SendWelcomeMessages();

                _logger.LogInformation("✅ WhatsApp Web connected successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling successful connection");
                await HandleConnectionFailureAsync();
            }
        }

        private async Task MonitorIncomingMessagesAsync()
        {
            _logger.LogInformation("Starting incoming message monitoring...");
            
            string? lastProcessedMessage = null;

            while (_isConnected && !_disposed && !(_monitoringCts?.Token.IsCancellationRequested ?? true))
            {
                try
                {
                    var newMessages = await GetUnreadMessagesAsync(lastProcessedMessage);
                    
                    foreach (var message in newMessages)
                    {
                        await HandleIncomingMessageAsync(message.Phone, message.Text, message.MessageId);
                        lastProcessedMessage = message.MessageId;
                    }

                    await Task.Delay(3000); // Check every 3 seconds
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error monitoring incoming messages");
                    if (_disposed) break;
                    await Task.Delay(5000);
                }
            }
        }

        private async Task<List<WhatsAppMessage>> GetUnreadMessagesAsync(string? lastProcessedId)
        {
            var messages = new List<WhatsAppMessage>();

            try
            {
                if (_driver == null || _disposed) return messages;

                await Task.Run(() =>
                {
                    try
                    {
                        // Find unread chats
                        var unreadChats = _driver.FindElements(By.CssSelector("[data-testid='cell-frame-container']"))
                            .Where(e => 
                            {
                                try
                                {
                                    var unreadBadge = e.FindElements(By.CssSelector("[data-testid='icon-unread-count'], .unread"));
                                    return unreadBadge.Any(b => b.Displayed);
                                }
                                catch
                                {
                                    return false;
                                }
                            })
                            .Take(5)
                            .ToList();

                        foreach (var chat in unreadChats)
                        {
                            try
                            {
                                chat.Click();
                                Thread.Sleep(2000); // Wait for messages to load

                                // Get the latest messages
                                var messageElements = _driver.FindElements(By.CssSelector(
                                    "[data-testid='msg-container'], .message-in, .message-out"
                                ));

                                if (messageElements.Count > 0)
                                {
                                    var latestMessage = messageElements[^1];
                                    var messageText = latestMessage.Text;
                                    var messageId = latestMessage.GetAttribute("data-id") ?? Guid.NewGuid().ToString();

                                    if (!string.IsNullOrEmpty(messageText) && messageId != lastProcessedId)
                                    {
                                        var phone = GetCurrentChatPhone();
                                        messages.Add(new WhatsAppMessage
                                        {
                                            Phone = phone,
                                            Text = messageText,
                                            MessageId = messageId
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error processing chat messages");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting unread messages");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetUnreadMessagesAsync");
            }

            return messages;
        }

        private string GetCurrentChatPhone()
        {
            try
            {
                if (_driver == null) return "unknown";
                
                var phoneSelectors = new[]
                {
                    "[data-testid='conversation-info-header-chat-title']",
                    ".chat-title",
                    "[title*='+']"
                };

                foreach (var selector in phoneSelectors)
                {
                    try
                    {
                        var element = _driver.FindElement(By.CssSelector(selector));
                        var text = element.Text;
                        if (!string.IsNullOrEmpty(text) && text.Contains("+"))
                        {
                            return text;
                        }
                    }
                    catch (NoSuchElementException) { }
                }

                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        public async Task HandleIncomingMessageAsync(string from, string message, string messageId)
        {
            try
            {
                _logger.LogInformation("📩 Received real WhatsApp message from {From}: {Message}", from, message);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // Find user by phone number
                var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == from);
                if (user == null)
                {
                    _logger.LogWarning("No user found with phone number: {PhoneNumber}", from);
                    await SendMessageAsync(from, "👋 Hello! I'm your AI Assistant. Please register in the app first to use all features.");
                    return;
                }

                var orchestrator = scope.ServiceProvider.GetRequiredService<ICommandOrchestrator>();
                var response = await orchestrator.HandleAsync(user.Id, message, "WhatsApp");
                
                // Send response back via WhatsApp
                await SendMessageAsync(from, response);
                
                // Trigger event
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(from, message, messageId);
                }

                _logger.LogInformation("✅ Processed and replied to message from {From}", from);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling incoming WhatsApp message from {From}", from);
                try
                {
                    await SendMessageAsync(from, "❌ Sorry, I encountered an error processing your message. Please try again.");
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Failed to send error message to {From}", from);
                }
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(string to, string text)
        {
            try
            {
                if (!_isConnected || _disposed)
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "WhatsApp is not connected. Please initialize connection first.",
                        Error = "NotConnected"
                    };
                }

                if (_wait == null || _driver == null) 
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "WebDriver is not initialized",
                        Error = "DriverNotInitialized"
                    };
                }

                _logger.LogInformation("Sending real WhatsApp message to {To}: {Text}", to, text);

                // Search for the contact
                await SearchAndOpenChat(to);
                
                // Type and send message
                var inputBox = _wait.Until(driver =>
                    driver.FindElement(By.CssSelector(
                        "[data-testid='conversation-compose-box-input'], " +
                        "[contenteditable='true'][data-tab='10'], " +
                        "div[contenteditable='true'][spellcheck='true']"
                    )));
                
                inputBox.Clear();
                inputBox.SendKeys(text);
                inputBox.SendKeys(Keys.Enter);

                _logger.LogInformation("✅ Real WhatsApp message sent to {To}", to);

                // Log message in database
                await LogMessageInDatabase(to, text, "outgoing");

                return new SendMessageResult
                {
                    Success = true,
                    Message = "Real WhatsApp message sent successfully",
                    MessageId = Guid.NewGuid().ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send real WhatsApp message to {To}", to);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Failed to send real WhatsApp message",
                    Error = ex.Message
                };
            }
        }

        private async Task SearchAndOpenChat(string phoneNumber)
        {
            try
            {
                if (_wait == null || _driver == null) throw new InvalidOperationException("WebDriver is not initialized");
                
                // Click search button
                var searchButton = _wait.Until(driver =>
                    driver.FindElement(By.CssSelector(
                        "[data-testid='search-button'], " +
                        "button[aria-label*='Search'], " +
                        "div[title*='Search']"
                    )));
                searchButton.Click();

                await Task.Delay(1000);

                // Type phone number in search
                var searchInput = _wait.Until(driver =>
                    driver.FindElement(By.CssSelector(
                        "[data-testid='search-input'], " +
                        "input[type='text'][placeholder*='Search'], " +
                        "div[contenteditable='true'][data-tab='3']"
                    )));
                
                searchInput.Clear();
                searchInput.SendKeys(phoneNumber);
                await Task.Delay(2000); // Wait for results

                // Click on the first result
                var firstResult = _wait.Until(driver =>
                    driver.FindElement(By.CssSelector(
                        "[data-testid='chat-list-item'], " +
                        "div[role='button'][tabindex='0']"
                    )));
                firstResult.Click();

                await Task.Delay(1000); // Wait for chat to open
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching and opening chat for {Phone}", phoneNumber);
                throw;
            }
        }

        private async Task LogMessageInDatabase(string to, string text, string direction)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == to);
                if (user != null)
                {
                    var messageLog = new Message
                    {
                        UserId = user.Id,
                        Body = $"WhatsApp: {text}",
                        Channel = "WhatsApp",
                        Direction = direction,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Messages.Add(messageLog);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log message in database");
            }
        }

        // Other interface implementations remain the same but with better error handling
        public async Task<SendMessageResult> SendMessageAsync(int userId, string text)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var user = await db.Users.FindAsync(userId);
                if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "User not found or phone number not set",
                        Error = "UserNotFound"
                    };
                }

                return await SendMessageAsync(user.PhoneNumber, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp message to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Failed to send message",
                    Error = ex.Message
                };
            }
        }

        public async Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions)
        {
            try
            {
                var actionText = "\n\n📋 *Quick Actions:*\n" + string.Join("\n", actions.Select((a, i) => $"{i + 1}. {a}"));
                return await SendMessageAsync(userId, message + actionText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send quick actions to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Failed to send quick actions",
                    Error = ex.Message
                };
            }
        }

        public async Task<bool> SendReminderAsync(int userId, string type, string title, DateTime dueTime, string description)
        {
            try
            {
                var emoji = type.ToLower() switch
                {
                    "task" => "✅",
                    "event" => "📅",
                    "meeting" => "👥",
                    _ => "⏰"
                };

                var message = $"{emoji} *Reminder: {type.ToUpper()}*\n\n" +
                             $"*{title}*\n" +
                             $"⏰ Due: {dueTime:MMM dd, yyyy 'at' HH:mm}\n" +
                             (string.IsNullOrEmpty(description) ? "" : $"\n📝 Details: {description}");

                var result = await SendMessageAsync(userId, message);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reminder to user {UserId}", userId);
                return false;
            }
        }

        private async Task SendWelcomeMessages()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var usersWithPhoneNumbers = await db.Users
                    .Where(u => !string.IsNullOrEmpty(u.PhoneNumber))
                    .ToListAsync();

                _logger.LogInformation("Found {Count} users with phone numbers for welcome messages", usersWithPhoneNumbers.Count);

                foreach (var user in usersWithPhoneNumbers)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(user.PhoneNumber))
                            continue;

                        var welcomeMessage = "✅ *WhatsApp Connected Successfully!*\n\n" +
                                           "🤖 *I'm your AI Assistant* \n" +
                                           "I can help you with:\n\n" +
                                           "• 📅 *Schedule meetings* - \"Schedule meeting tomorrow at 3 PM\"\n" +
                                           "• ✅ *Create tasks* - \"Create task for project report\"\n" +
                                           "• ⏰ *Set reminders* - \"Remind me to call John at 5 PM\"\n" +
                                           "• 📋 *Check calendar* - \"What's on my calendar today?\"\n" +
                                           "• 📧 *Email alerts* - Get notified about important emails\n\n" +
                                           "Just send me a message and I'll help you! 🚀";

                        var result = await SendMessageAsync(user.PhoneNumber, welcomeMessage);
                        
                        if (result.Success)
                        {
                            _logger.LogInformation("✅ Sent welcome message to user {UserId} at {PhoneNumber}", user.Id, user.PhoneNumber);
                        }
                        
                        await Task.Delay(2000); // Rate limiting
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "❌ Failed to send welcome message to user {UserId}", user.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in SendWelcomeMessages");
            }
        }

        private async Task UpdateConnectionStatusInDatabase(bool isConnected)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var session = await db.WhatsAppSessions.FirstOrDefaultAsync();
                if (session == null)
                {
                    session = new WhatsAppSession
                    {
                        IsConnected = isConnected,
                        ConnectedAt = isConnected ? DateTime.UtcNow : null,
                        LastCheckedAt = DateTime.UtcNow
                    };
                    db.WhatsAppSessions.Add(session);
                }
                else
                {
                    session.IsConnected = isConnected;
                    session.ConnectedAt = isConnected ? DateTime.UtcNow : session.ConnectedAt;
                    session.LastCheckedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
                _logger.LogInformation("WhatsApp connection status updated to: {Status}", 
                    isConnected ? "Connected" : "Disconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update connection status in database");
            }
        }

        private async Task HandleConnectionTimeoutAsync()
        {
            _logger.LogWarning("WhatsApp connection timed out");
            _isInitializing = false;
            await UpdateConnectionStatusInDatabase(false);
        }

        private async Task HandleConnectionFailureAsync()
        {
            _isConnected = false;
            _isInitializing = false;
            await UpdateConnectionStatusInDatabase(false);
        }

        public Task<bool> CheckConnectionStatusAsync()
        {
            try
            {
                if (!_isConnected || _disposed) return Task.FromResult(false);
                
                // Verify we're still connected by checking if main interface is visible
                return IsConnectedToWhatsAppAsync();
            }
            catch
            {
                _isConnected = false;
                return Task.FromResult(false);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _monitoringCts?.Cancel();
                _isConnected = false;
                _isInitializing = false;
                _qrData = null;
                _qrGeneratedAt = null;
                
                // Close the browser safely
                CleanupDriver();
                
                await UpdateConnectionStatusInDatabase(false);
                _logger.LogInformation("WhatsApp disconnected successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting WhatsApp");
            }
        }

        public async Task CleanupAsync()
        {
            await DisconnectAsync();
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            _monitoringCts?.Cancel();
            _monitoringCts?.Dispose();
            
            CleanupDriver();
        }

        public void RegisterMessageHandler(Func<string, string, string, Task> handler)
        {
            OnMessageReceived += handler;
        }
    }

    // Helper class for WhatsApp messages
    public class WhatsAppMessage
    {
        public string Phone { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
    }
}