/**
 * Virtual Controller - WebSocket Client
 * Minimal latency touch controller for mobile browsers
 */

class VirtualController {
    constructor() {
        this.ws = null;
        this.playerName = '';
        this.playerSlot = 0;
        this.isConnected = false;
        this.buttonLayout = 'xbox'; // 'xbox' or 'playstation'
        
        // iOS detection
        this.isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent) || 
                     (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        this.isStandalone = window.navigator.standalone === true || 
                            window.matchMedia('(display-mode: standalone)').matches;
        this.isSafari = /^((?!chrome|android).)*safari/i.test(navigator.userAgent);
        
        // Show iOS install prompt if on Safari and not standalone
        if (this.isIOS && !this.isStandalone) {
            this.showIOSInstallPrompt();
        }
        
        // Fullscreen and edit mode
        this.isFullscreen = false;
        this.isEditMode = false;
        this.isSplitMode = false;
        this.controlScale = 100;
        this.selectedGroup = null; // Currently selected group for size adjustment
        
        // Hold button tracking
        this.holdTimers = {};
        this.holdProgress = {};
        this.HOLD_DURATION_FULLSCREEN = 1000; // 1 second for fullscreen
        this.HOLD_DURATION_SETTINGS = 500; // 0.5 seconds for settings
        
        // Dragging state
        this.dragState = {
            active: false,
            element: null,
            startX: 0,
            startY: 0,
            offsetX: 0,
            offsetY: 0,
            touchId: null
        };
        
        // Resize state
        this.resizeState = null;
        
        // Saved positions for each control group
        this.savedPositions = this.loadPositions();
        
        // Controller state - sent on every change
        this.state = {
            type: 'input',
            a: false, b: false, x: false, y: false,
            lb: false, rb: false,
            lt: 0, rt: 0,
            back: false, start: false, guide: false,
            up: false, down: false, left: false, right: false,
            ls: false, rs: false,
            lx: 0, ly: 0,
            rx: 0, ry: 0
        };
        
        // Stick tracking - added lastUpdate for drift detection
        this.leftStick = { active: false, startX: 0, startY: 0, touchId: null, lastUpdate: 0 };
        this.rightStick = { active: false, startX: 0, startY: 0, touchId: null, lastUpdate: 0 };
        
        // Trigger tracking
        this.leftTrigger = { active: false, startY: 0, touchId: null };
        this.rightTrigger = { active: false, startY: 0, touchId: null };
        
        // Current layout settings (from server)
        this.currentLayout = {
            dpad: true,
            face: true,
            lstick: true,
            rstick: true,
            bumpers: true,
            triggers: true,
            startback: true,
            guide: true
        };
        
        // Saved layout hash for matching phone settings to server layout
        this.savedLayoutHash = localStorage.getItem('layoutHash') || '';
        
        this.init();
    }
    
    // Generate a hash of the layout for matching
    getLayoutHash(layout) {
        return `${layout.dpad}-${layout.face}-${layout.lstick}-${layout.rstick}-${layout.bumpers}-${layout.triggers}-${layout.startback}-${layout.guide}`;
    }
    
    // Save phone settings keyed by layout hash
    savePhoneSettings() {
        const hash = this.getLayoutHash(this.currentLayout);
        const settings = {
            positions: this.savedPositions,
            scale: this.controlScale,
            splitMode: this.isSplitMode,
            buttonLayout: this.buttonLayout
        };
        
        try {
            // Save current settings for this layout
            localStorage.setItem(`phoneSettings_${hash}`, JSON.stringify(settings));
            localStorage.setItem('layoutHash', hash);
            localStorage.setItem('lastPhoneSettings', JSON.stringify(settings));
        } catch (e) {
            console.warn('Failed to save phone settings:', e);
        }
    }
    
    // Load phone settings for the current layout
    loadPhoneSettingsForLayout() {
        const hash = this.getLayoutHash(this.currentLayout);
        
        try {
            // Try to load settings for this specific layout
            const saved = localStorage.getItem(`phoneSettings_${hash}`);
            if (saved) {
                const settings = JSON.parse(saved);
                this.savedPositions = settings.positions || {};
                this.controlScale = settings.scale || 100;
                this.isSplitMode = settings.splitMode || false;
                if (settings.buttonLayout) {
                    this.setButtonLayout(settings.buttonLayout);
                }
                
                // Apply settings
                if (this.isFullscreen) {
                    this.applyPositions();
                }
                if (this.isSplitMode) {
                    this.screens.controller.classList.add('split-mode');
                    if (this.elements.splitFaceBtn) {
                        this.elements.splitFaceBtn.textContent = 'Merge Buttons';
                    }
                }
                
                return true;
            }
        } catch (e) {
            console.warn('Failed to load phone settings:', e);
        }
        return false;
    }
    
    init() {
        // UI Elements
        this.screens = {
            connect: document.getElementById('connect-screen'),
            reconnect: document.getElementById('reconnect-screen'),
            controller: document.getElementById('controller-screen')
        };
        
        this.elements = {
            playerName: document.getElementById('player-name'),
            connectBtn: document.getElementById('connect-btn'),
            connectionStatus: document.getElementById('connection-status'),
            backBtn: document.getElementById('back-btn'),
            reconnectBtn: document.getElementById('reconnect-btn'),
            playerSlot: document.getElementById('player-slot'),
            playerDisplayName: document.getElementById('player-display-name'),
            wsStatus: document.getElementById('ws-status'),
            leftStick: document.getElementById('left-stick'),
            rightStick: document.getElementById('right-stick'),
            leftStickArea: document.getElementById('left-stick-container'),
            rightStickArea: document.getElementById('right-stick-container'),
            controllerType: document.getElementById('controller-type'),
            toggleLayoutBtn: document.getElementById('toggle-layout-btn'),
            // Fullscreen elements
            fullscreenToggleBtn: document.getElementById('fullscreen-toggle-btn'),
            settingsToggleBtn: document.getElementById('settings-toggle-btn'),
            fsProgressRing: document.getElementById('fs-progress-ring'),
            settingsProgressRing: document.getElementById('settings-progress-ring'),
            fsPlayerSlot: document.getElementById('fs-player-slot'),
            fsPlayerName: document.getElementById('fs-player-name'),
            fsControllerType: document.getElementById('fs-controller-type'),
            fsStatus: document.getElementById('fs-status'),
            sizeLabel: document.getElementById('size-label'),
            sizeIncrease: document.getElementById('size-increase'),
            sizeDecrease: document.getElementById('size-decrease'),
            controllerWrapper: document.getElementById('controller-wrapper'),
            editModeIndicator: document.getElementById('edit-mode-indicator'),
            resetLayoutBtn: document.getElementById('reset-layout-btn'),
            doneEditBtn: document.getElementById('done-edit-btn'),
            splitFaceBtn: document.getElementById('split-face-btn'),
            groupSizeControl: document.getElementById('group-size-control'),
            groupSizeName: document.getElementById('group-size-name'),
            groupSizeValue: document.getElementById('group-size-value'),
            groupSizeIncrease: document.getElementById('group-size-increase'),
            groupSizeDecrease: document.getElementById('group-size-decrease'),
            fixFullscreenBtn: document.getElementById('fix-fullscreen-btn')
        };
        
        // Event listeners
        this.elements.connectBtn.addEventListener('click', () => this.connect());
        this.elements.backBtn.addEventListener('click', () => this.showScreen('connect'));
        this.elements.reconnectBtn.addEventListener('click', () => this.showScreen('reconnect'));
        this.elements.toggleLayoutBtn.addEventListener('click', () => this.toggleButtonLayout());
        
        // Fullscreen hold button events (with different durations)
        this.setupHoldButton(this.elements.fullscreenToggleBtn, 'fs-progress-ring', () => this.toggleFullscreen(), this.HOLD_DURATION_FULLSCREEN);
        this.setupHoldButton(this.elements.settingsToggleBtn, 'settings-progress-ring', () => this.toggleEditMode(), this.HOLD_DURATION_SETTINGS);
        
        // Size controls
        this.elements.sizeIncrease?.addEventListener('click', () => this.adjustScale(10));
        this.elements.sizeDecrease?.addEventListener('click', () => this.adjustScale(-10));
        
        // Edit mode buttons
        this.elements.resetLayoutBtn?.addEventListener('click', () => this.resetLayout());
        this.elements.doneEditBtn?.addEventListener('click', () => this.toggleEditMode());
        this.elements.splitFaceBtn?.addEventListener('click', () => this.toggleSplitMode());
        
        // Per-group size controls
        this.elements.groupSizeIncrease?.addEventListener('click', () => this.adjustGroupScale(10));
        this.elements.groupSizeDecrease?.addEventListener('click', () => this.adjustGroupScale(-10));
        
        // Fix fullscreen button (re-enters fullscreen when tapped)
        this.elements.fixFullscreenBtn?.addEventListener('click', () => this.enterBrowserFullscreen());
        
        // Slot buttons
        document.querySelectorAll('.slot-btn').forEach(btn => {
            btn.addEventListener('click', () => this.reconnect(parseInt(btn.dataset.slot)));
        });
        
        // Enter key to connect
        this.elements.playerName.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') this.connect();
        });
        
        // Prevent zoom on double tap
        document.addEventListener('touchstart', (e) => {
            if (e.touches.length > 1) e.preventDefault();
        }, { passive: false });
        
        // Setup controller inputs
        this.setupButtons();
        this.setupSticks();
        this.setupTriggers();
        this.setupDragging();
        this.setupResizing();
        
        // Apply saved split mode state (after DOM is ready)
        if (this.isSplitMode) {
            this.screens.controller.classList.add('split-mode');
            if (this.elements.splitFaceBtn) {
                this.elements.splitFaceBtn.textContent = 'Merge Buttons';
            }
        }
        
        // Load saved name
        const savedName = localStorage.getItem('playerName');
        if (savedName) {
            this.elements.playerName.value = savedName;
        }
    }
    
    showScreen(name) {
        Object.values(this.screens).forEach(s => s.classList.remove('active'));
        this.screens[name].classList.add('active');
    }
    
    showStatus(message, isError = false) {
        this.elements.connectionStatus.textContent = message;
        this.elements.connectionStatus.className = 'status ' + (isError ? 'error' : 'success');
    }
    
    connect() {
        this.playerName = this.elements.playerName.value.trim() || 'Player';
        localStorage.setItem('playerName', this.playerName);
        
        this.elements.connectBtn.disabled = true;
        this.showStatus('Connecting...');
        
        this.initWebSocket(() => {
            // Send connect message
            this.send({ type: 'connect', name: this.playerName });
        });
    }
    
    reconnect(slot) {
        this.playerName = localStorage.getItem('playerName') || 'Player';
        
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.send({ type: 'reconnect', slot: slot, name: this.playerName });
        } else {
            this.initWebSocket(() => {
                this.send({ type: 'reconnect', slot: slot, name: this.playerName });
            });
        }
    }
    
    initWebSocket(onOpen) {
        // Close existing connection
        if (this.ws) {
            this.ws.close();
        }
        
        // Connect to WebSocket server (port + 1)
        const wsPort = parseInt(location.port) + 1;
        const wsUrl = `ws://${location.hostname}:${wsPort}/`;
        
        try {
            this.ws = new WebSocket(wsUrl);
            this.ws.binaryType = 'arraybuffer';
            
            this.ws.onopen = () => {
                console.log('WebSocket connected');
                this.isConnected = true;
                if (onOpen) onOpen();
            };
            
            this.ws.onmessage = (event) => {
                this.handleMessage(JSON.parse(event.data));
            };
            
            this.ws.onerror = (error) => {
                console.error('WebSocket error:', error);
                this.showStatus('Connection error', true);
                this.elements.connectBtn.disabled = false;
            };
            
            this.ws.onclose = () => {
                console.log('WebSocket closed');
                this.isConnected = false;
                this.updateConnectionStatus(false);
                
                // Auto-reconnect if on controller screen
                if (this.screens.controller.classList.contains('active')) {
                    setTimeout(() => this.reconnect(this.playerSlot), 2000);
                }
            };
        } catch (e) {
            this.showStatus('Failed to connect: ' + e.message, true);
            this.elements.connectBtn.disabled = false;
        }
    }
    
    handleMessage(data) {
        switch (data.type) {
            case 'assigned':
                this.playerSlot = data.slot;
                this.playerName = data.name;
                this.elements.playerSlot.textContent = 'P' + data.slot;
                this.elements.playerDisplayName.textContent = data.name;
                this.showScreen('controller');
                this.updateConnectionStatus(true);
                
                // Update fullscreen info
                this.updateFullscreenInfo();
                
                // Auto-detect controller type based on slot (5-8 are DualShock 4)
                const isPlayStation = data.slot >= 5;
                this.setButtonLayout(isPlayStation ? 'playstation' : 'xbox');
                
                // Haptic feedback
                this.hapticFeedback('medium');
                break;
                
            case 'error':
                this.showStatus(data.message, true);
                this.elements.connectBtn.disabled = false;
                break;
            
            case 'layout':
                this.applyLayout(data);
                break;
            
            case 'kicked':
                this.handleKicked(data.message);
                break;
        }
    }
    
    toggleButtonLayout() {
        const newLayout = this.buttonLayout === 'xbox' ? 'playstation' : 'xbox';
        this.setButtonLayout(newLayout);
        
        // Haptic feedback
        this.hapticFeedback('light');
    }
    
    setButtonLayout(layout) {
        this.buttonLayout = layout;
        
        // Update controller type badge (normal mode)
        const typeBadge = this.elements.controllerType;
        typeBadge.textContent = layout === 'xbox' ? 'Xbox' : 'PS4';
        typeBadge.className = 'controller-type-badge ' + (layout === 'xbox' ? 'xbox' : 'playstation');
        
        // Update fullscreen type badge
        if (this.elements.fsControllerType) {
            this.elements.fsControllerType.textContent = layout === 'xbox' ? 'Xbox' : 'PS4';
            this.elements.fsControllerType.className = 'fs-type-badge ' + (layout === 'xbox' ? 'xbox' : 'playstation');
        }
        
        // Update controller class for button colors
        const controller = document.querySelector('.controller');
        controller.classList.remove('xbox', 'playstation');
        controller.classList.add(layout);
        
        // Update all button labels
        document.querySelectorAll('[data-xbox][data-ps]').forEach(el => {
            const label = layout === 'xbox' ? el.dataset.xbox : el.dataset.ps;
            el.textContent = label;
        });
    }
    
    // ============================================
    // FULLSCREEN MODE
    // ============================================
    
    setupHoldButton(button, progressRingId, callback, holdDuration) {
        if (!button) return;
        
        const progressRing = document.getElementById(progressRingId);
        const circumference = 138.2; // 2 * PI * 22
        let holdStart = null;
        let animationFrame = null;
        
        const updateProgress = () => {
            if (!holdStart) return;
            
            const elapsed = Date.now() - holdStart;
            const progress = Math.min(elapsed / holdDuration, 1);
            
            if (progressRing) {
                progressRing.style.strokeDashoffset = circumference * (1 - progress);
            }
            
            if (progress >= 1) {
                // Completed hold
                this.resetHoldProgress(progressRing, circumference);
                holdStart = null;
                this.hapticFeedback('heavy');
                callback();
            } else {
                animationFrame = requestAnimationFrame(updateProgress);
            }
        };
        
        const startHold = (e) => {
            e.preventDefault();
            holdStart = Date.now();
            animationFrame = requestAnimationFrame(updateProgress);
            this.hapticFeedback('light');
        };
        
        const endHold = () => {
            holdStart = null;
            if (animationFrame) {
                cancelAnimationFrame(animationFrame);
            }
            this.resetHoldProgress(progressRing, circumference);
        };
        
        // Touch events
        button.addEventListener('touchstart', startHold, { passive: false });
        button.addEventListener('touchend', endHold);
        button.addEventListener('touchcancel', endHold);
        
        // Mouse events
        button.addEventListener('mousedown', startHold);
        button.addEventListener('mouseup', endHold);
        button.addEventListener('mouseleave', endHold);
    }
    
    resetHoldProgress(progressRing, circumference) {
        if (progressRing) {
            progressRing.style.strokeDashoffset = circumference;
        }
    }
    
    toggleFullscreen() {
        this.isFullscreen = !this.isFullscreen;
        this.screens.controller.classList.toggle('fullscreen-mode', this.isFullscreen);
        
        if (this.isFullscreen) {
            // Request actual browser fullscreen (hides address bar)
            this.enterBrowserFullscreen();
            
            // Apply positions after a short delay to let fullscreen settle
            setTimeout(() => {
                this.applyPositions();
                this.applyScale();
                this.updateFullscreenInfo();
            }, 100);
        } else {
            // Exit edit mode if active
            if (this.isEditMode) {
                this.isEditMode = false;
                this.screens.controller.classList.remove('edit-mode');
            }
            
            // Reset control positions to default (CSS will handle normal layout)
            this.resetControlPositions();
            
            // Exit actual fullscreen
            this.exitBrowserFullscreen();
        }
    }
    
    enterBrowserFullscreen() {
        // iOS Safari doesn't support the Fullscreen API
        // For iOS, we rely on PWA standalone mode or just maximize what we can
        if (this.isIOS) {
            // On iOS, add a class to simulate fullscreen by hiding browser UI elements
            document.body.classList.add('ios-fullscreen');
            // Scroll to hide address bar
            window.scrollTo(0, 1);
            return;
        }
        
        const elem = document.documentElement;
        if (elem.requestFullscreen) {
            elem.requestFullscreen().catch(() => {});
        } else if (elem.webkitRequestFullscreen) {
            elem.webkitRequestFullscreen();
        } else if (elem.msRequestFullscreen) {
            elem.msRequestFullscreen();
        } else if (elem.mozRequestFullScreen) {
            elem.mozRequestFullScreen();
        }
    }
    
    exitBrowserFullscreen() {
        if (this.isIOS) {
            document.body.classList.remove('ios-fullscreen');
            return;
        }
        
        if (document.exitFullscreen) {
            document.exitFullscreen().catch(() => {});
        } else if (document.webkitExitFullscreen) {
            document.webkitExitFullscreen();
        } else if (document.msExitFullscreen) {
            document.msExitFullscreen();
        } else if (document.mozCancelFullScreen) {
            document.mozCancelFullScreen();
        }
    }
    
    // Cross-platform haptic feedback (works on iOS and Android)
    hapticFeedback(style = 'light') {
        // Try iOS-specific haptic feedback first
        if (this.isIOS && window.navigator && 'vibrate' in window.navigator === false) {
            // iOS doesn't have navigator.vibrate, but we can try AudioContext click
            // or just skip - iOS Safari doesn't support vibration
            return;
        }
        
        // Android and other devices with vibration support
        if (navigator.vibrate) {
            switch (style) {
                case 'light':
                    navigator.vibrate(10);
                    break;
                case 'medium':
                    navigator.vibrate([20, 20, 20]);
                    break;
                case 'heavy':
                    navigator.vibrate([50, 30, 50]);
                    break;
                default:
                    navigator.vibrate(10);
            }
        }
    }
    
    // Show iOS install prompt for Add to Home Screen
    showIOSInstallPrompt() {
        // Don't show if user dismissed it before
        if (localStorage.getItem('iosPromptDismissed')) return;
        
        const prompt = document.createElement('div');
        prompt.id = 'ios-install-prompt';
        prompt.className = 'ios-install-prompt';
        prompt.innerHTML = `
            <div class="ios-prompt-content">
                <span class="ios-prompt-icon">📲</span>
                <div class="ios-prompt-text">
                    <strong>Install Virtual Controller</strong>
                    <p>Tap <span class="ios-share-icon">⬆</span> then "Add to Home Screen" for the best experience</p>
                </div>
                <button class="ios-prompt-close" id="ios-prompt-close">✕</button>
            </div>
        `;
        document.body.appendChild(prompt);
        
        document.getElementById('ios-prompt-close').addEventListener('click', () => {
            prompt.remove();
            localStorage.setItem('iosPromptDismissed', 'true');
        });
        
        // Auto-hide after 15 seconds
        setTimeout(() => {
            if (prompt.parentNode) {
                prompt.classList.add('hiding');
                setTimeout(() => prompt.remove(), 500);
            }
        }, 15000);
    }
    
    // Reset control positions when exiting fullscreen
    resetControlPositions() {
        const groups = document.querySelectorAll('.draggable-group');
        groups.forEach(group => {
            group.style.left = '';
            group.style.top = '';
            group.style.right = '';
            group.style.bottom = '';
            group.style.position = '';
        });
    }
    
    updateFullscreenInfo() {
        if (this.elements.fsPlayerSlot) {
            this.elements.fsPlayerSlot.textContent = 'P' + this.playerSlot;
        }
        if (this.elements.fsPlayerName) {
            this.elements.fsPlayerName.textContent = this.playerName;
        }
        if (this.elements.fsStatus) {
            this.elements.fsStatus.className = 'fs-status ' + (this.isConnected ? 'connected' : 'disconnected');
        }
    }
    
    // ============================================
    // EDIT MODE & DRAGGING
    // ============================================
    
    toggleEditMode() {
        // Edit mode works even without fullscreen now
        this.isEditMode = !this.isEditMode;
        this.screens.controller.classList.toggle('edit-mode', this.isEditMode);
        
        // If entering edit mode but not in fullscreen, enter fullscreen first
        if (this.isEditMode && !this.isFullscreen) {
            this.isFullscreen = true;
            this.screens.controller.classList.add('fullscreen-mode');
            
            // Request actual browser fullscreen
            this.enterBrowserFullscreen();
            
            setTimeout(() => {
                this.applyPositions();
                this.applyScale();
                this.updateFullscreenInfo();
            }, 100);
        }
        
        this.hapticFeedback(this.isEditMode ? 'heavy' : 'light');
        
        // Deselect any selected group when exiting edit mode
        if (!this.isEditMode) {
            this.deselectGroup();
        }
    }
    
    toggleSplitMode() {
        this.isSplitMode = !this.isSplitMode;
        this.screens.controller.classList.toggle('split-mode', this.isSplitMode);
        
        // Update split button text
        if (this.elements.splitFaceBtn) {
            this.elements.splitFaceBtn.textContent = this.isSplitMode ? 'Merge Buttons' : 'Split Buttons';
        }
        
        // Apply button layout to update labels on split buttons
        this.setButtonLayout(this.buttonLayout);
        
        // Apply positions to new individual buttons
        if (this.isSplitMode) {
            setTimeout(() => this.applyPositions(), 50);
        }
        
        // Save split mode state
        this.savePositions();
        
        this.hapticFeedback('medium');
    }
    
    selectGroup(group) {
        // Deselect previous
        this.deselectGroup();
        
        // Select new
        this.selectedGroup = group;
        group.classList.add('selected');
        
        // Show size control panel
        const sizeControl = this.elements.groupSizeControl;
        if (sizeControl) {
            sizeControl.classList.add('visible');
            
            // Update name and value
            const groupName = this.getGroupDisplayName(group.dataset.group);
            const scale = parseInt(group.dataset.scale) || 100;
            
            if (this.elements.groupSizeName) {
                this.elements.groupSizeName.textContent = groupName;
            }
            if (this.elements.groupSizeValue) {
                this.elements.groupSizeValue.textContent = scale + '%';
            }
        }
    }
    
    deselectGroup() {
        if (this.selectedGroup) {
            this.selectedGroup.classList.remove('selected');
            this.selectedGroup = null;
        }
        
        // Hide size control panel
        if (this.elements.groupSizeControl) {
            this.elements.groupSizeControl.classList.remove('visible');
        }
    }
    
    getGroupDisplayName(groupId) {
        const names = {
            'top': 'Top Row',
            'dpad': 'D-Pad',
            'lstick': 'Left Stick',
            'face': 'Face Buttons',
            'rstick': 'Right Stick',
            'btn-a': 'A Button',
            'btn-b': 'B Button',
            'btn-x': 'X Button',
            'btn-y': 'Y Button'
        };
        return names[groupId] || groupId;
    }
    
    adjustGroupScale(delta) {
        if (!this.selectedGroup) return;
        
        let scale = parseInt(this.selectedGroup.dataset.scale) || 100;
        scale = Math.max(50, Math.min(200, scale + delta));
        
        this.selectedGroup.dataset.scale = scale;
        
        if (this.elements.groupSizeValue) {
            this.elements.groupSizeValue.textContent = scale + '%';
        }
        
        // Save to positions
        const groupId = this.selectedGroup.dataset.group;
        if (!this.savedPositions[groupId]) {
            this.savedPositions[groupId] = {};
        }
        this.savedPositions[groupId].scale = scale;
        this.savePositions();
        
        this.hapticFeedback('light');
    }
    
    setupDragging() {
        const groups = document.querySelectorAll('.draggable-group');
        
        groups.forEach(group => {
            const handle = group.querySelector('.drag-handle');
            if (!handle) return;
            
            // Touch events on handle
            handle.addEventListener('touchstart', (e) => this.startDrag(e, group), { passive: false });
            
            // Mouse events on handle
            handle.addEventListener('mousedown', (e) => this.startDrag(e, group));
            
            // Tap on group content to select for size adjustment
            group.addEventListener('click', (e) => {
                if (this.isEditMode && !this.dragState.active) {
                    // Don't select if clicking on the drag handle (that's for dragging)
                    if (!e.target.classList.contains('drag-handle')) {
                        this.selectGroup(group);
                    }
                }
            });
        });
        
        // Click outside to deselect
        document.addEventListener('click', (e) => {
            if (this.isEditMode && this.selectedGroup) {
                const clickedGroup = e.target.closest('.draggable-group');
                const clickedSizeControl = e.target.closest('.group-size-control');
                if (!clickedGroup && !clickedSizeControl) {
                    this.deselectGroup();
                }
            }
        });
        
        // Global move and end events
        document.addEventListener('touchmove', (e) => this.handleMove(e), { passive: false });
        document.addEventListener('touchend', (e) => this.handleEnd(e));
        document.addEventListener('touchcancel', (e) => this.handleEnd(e));
        
        document.addEventListener('mousemove', (e) => this.handleMove(e));
        document.addEventListener('mouseup', (e) => this.handleEnd(e));
    }
    
    setupResizing() {
        const groups = document.querySelectorAll('.draggable-group');
        
        groups.forEach(group => {
            const resizeHandle = group.querySelector('.resize-handle');
            if (!resizeHandle) return;
            
            // Touch events on resize handle
            resizeHandle.addEventListener('touchstart', (e) => this.startResize(e, group), { passive: false });
            
            // Mouse events on resize handle
            resizeHandle.addEventListener('mousedown', (e) => this.startResize(e, group));
        });
    }
    
    startResize(e, group) {
        if (!this.isEditMode) return;
        
        e.preventDefault();
        e.stopPropagation();
        
        const isTouch = e.type === 'touchstart';
        const point = isTouch ? e.touches[0] : e;
        
        const currentScale = parseInt(group.dataset.scale) || 100;
        
        this.resizeState = {
            active: true,
            element: group,
            startX: point.clientX,
            startY: point.clientY,
            startScale: currentScale,
            touchId: isTouch ? e.touches[0].identifier : null
        };
        
        // Select this group for visual feedback
        this.selectGroup(group);
        
        if (navigator.vibrate) navigator.vibrate(15);
    }
    
    handleMove(e) {
        if (this.dragState.active) {
            this.moveDrag(e);
        } else if (this.resizeState && this.resizeState.active) {
            this.moveResize(e);
        }
    }
    
    handleEnd(e) {
        if (this.dragState.active) {
            this.endDrag(e);
        } else if (this.resizeState && this.resizeState.active) {
            this.endResize(e);
        }
    }
    
    moveResize(e) {
        if (!this.resizeState || !this.resizeState.active) return;
        
        const isTouch = e.type === 'touchmove';
        let point;
        
        if (isTouch) {
            for (const touch of e.changedTouches) {
                if (touch.identifier === this.resizeState.touchId) {
                    point = touch;
                    break;
                }
            }
            if (!point) return;
            e.preventDefault();
        } else {
            point = e;
        }
        
        // Calculate scale change based on diagonal drag distance
        const dx = point.clientX - this.resizeState.startX;
        const dy = point.clientY - this.resizeState.startY;
        const distance = (dx + dy) / 2; // Average of both directions
        
        // Each 20px = 10% scale change
        const scaleChange = Math.round(distance / 20) * 10;
        const newScale = Math.max(50, Math.min(200, this.resizeState.startScale + scaleChange));
        
        this.resizeState.element.dataset.scale = newScale;
        
        // Update size display
        if (this.elements.groupSizeValue) {
            this.elements.groupSizeValue.textContent = newScale + '%';
        }
    }
    
    endResize(e) {
        if (!this.resizeState || !this.resizeState.active) return;
        
        const isTouch = e.type === 'touchend' || e.type === 'touchcancel';
        
        if (isTouch) {
            let found = false;
            for (const touch of e.changedTouches) {
                if (touch.identifier === this.resizeState.touchId) {
                    found = true;
                    break;
                }
            }
            if (!found) return;
        }
        
        // Save the scale
        const groupId = this.resizeState.element.dataset.group;
        const newScale = parseInt(this.resizeState.element.dataset.scale) || 100;
        
        if (!this.savedPositions[groupId]) {
            this.savedPositions[groupId] = {};
        }
        this.savedPositions[groupId].scale = newScale;
        this.savePositions();
        
        this.resizeState.active = false;
        this.resizeState = null;
    }
    
    startDrag(e, group) {
        if (!this.isEditMode) return;
        
        e.preventDefault();
        e.stopPropagation();
        
        const isTouch = e.type === 'touchstart';
        const point = isTouch ? e.touches[0] : e;
        
        const rect = group.getBoundingClientRect();
        
        this.dragState = {
            active: true,
            element: group,
            startX: point.clientX,
            startY: point.clientY,
            offsetX: rect.left,
            offsetY: rect.top,
            touchId: isTouch ? e.touches[0].identifier : null
        };
        
        group.classList.add('dragging');
        if (navigator.vibrate) navigator.vibrate(15);
    }
    
    moveDrag(e) {
        if (!this.dragState.active) return;
        
        const isTouch = e.type === 'touchmove';
        let point;
        
        if (isTouch) {
            for (const touch of e.changedTouches) {
                if (touch.identifier === this.dragState.touchId) {
                    point = touch;
                    break;
                }
            }
            if (!point) return;
            e.preventDefault();
        } else {
            point = e;
        }
        
        const dx = point.clientX - this.dragState.startX;
        const dy = point.clientY - this.dragState.startY;
        
        const newX = this.dragState.offsetX + dx;
        const newY = this.dragState.offsetY + dy;
        
        this.dragState.element.style.left = newX + 'px';
        this.dragState.element.style.top = newY + 'px';
        this.dragState.element.style.right = 'auto';
        this.dragState.element.style.bottom = 'auto';
    }
    
    endDrag(e) {
        if (!this.dragState.active) return;
        
        const isTouch = e.type === 'touchend' || e.type === 'touchcancel';
        
        if (isTouch) {
            let found = false;
            for (const touch of e.changedTouches) {
                if (touch.identifier === this.dragState.touchId) {
                    found = true;
                    break;
                }
            }
            if (!found) return;
        }
        
        this.dragState.element.classList.remove('dragging');
        
        // Save position
        const groupId = this.dragState.element.dataset.group;
        const rect = this.dragState.element.getBoundingClientRect();
        
        this.savedPositions[groupId] = {
            left: rect.left,
            top: rect.top
        };
        this.savePositions();
        
        this.dragState.active = false;
        this.dragState.element = null;
    }
    
    // ============================================
    // SCALE CONTROLS
    // ============================================
    
    adjustScale(delta) {
        this.controlScale = Math.max(50, Math.min(150, this.controlScale + delta));
        this.applyScale();
        this.savePositions();
        
        this.hapticFeedback('light');
    }
    
    applyScale() {
        if (this.elements.controllerWrapper) {
            this.elements.controllerWrapper.dataset.scale = this.controlScale;
        }
        if (this.elements.sizeLabel) {
            this.elements.sizeLabel.textContent = this.controlScale + '%';
        }
    }
    
    // ============================================
    // POSITION PERSISTENCE
    // ============================================
    
    loadPositions() {
        try {
            const saved = localStorage.getItem('controllerPositions');
            if (saved) {
                const data = JSON.parse(saved);
                this.controlScale = data.scale || 100;
                // Only set split mode if explicitly saved as true
                this.isSplitMode = data.splitMode === true ? true : false;
                
                return data.positions || {};
            }
        } catch (e) {
            console.warn('Failed to load positions:', e);
        }
        // Ensure split mode is false by default
        this.isSplitMode = false;
        return {};
    }
    
    savePositions() {
        try {
            localStorage.setItem('controllerPositions', JSON.stringify({
                positions: this.savedPositions,
                scale: this.controlScale,
                splitMode: this.isSplitMode
            }));
            
            // Also save phone settings keyed by layout
            this.savePhoneSettings();
        } catch (e) {
            console.warn('Failed to save positions:', e);
        }
    }
    
    applyPositions() {
        const groups = document.querySelectorAll('.draggable-group');
        const container = this.elements.controllerWrapper;
        
        if (!container) return;
        
        groups.forEach(group => {
            const groupId = group.dataset.group;
            const saved = this.savedPositions[groupId];
            
            if (saved) {
                // Apply position
                if (saved.left !== undefined && saved.top !== undefined) {
                    group.style.left = saved.left + 'px';
                    group.style.top = saved.top + 'px';
                    group.style.right = 'auto';
                    group.style.bottom = 'auto';
                } else {
                    this.setDefaultPosition(group, groupId);
                }
                
                // Apply scale
                if (saved.scale !== undefined) {
                    group.dataset.scale = saved.scale;
                }
            } else {
                // Set default positions based on group
                this.setDefaultPosition(group, groupId);
            }
        });
    }
    
    setDefaultPosition(group, groupId) {
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        
        // Better centered default positions
        const defaults = {
            'top': { left: (vw - 300) / 2, top: 55 },
            'dpad': { left: 20, top: vh - 200 },
            'lstick': { left: 20, top: vh / 2 - 60 },
            'face': { left: vw - 150, top: vh - 200 },
            'rstick': { left: vw - 150, top: vh / 2 - 60 },
            // Individual face buttons (diamond pattern on right side)
            'btn-a': { left: vw - 100, top: vh - 100 },  // Bottom
            'btn-b': { left: vw - 50, top: vh - 150 },   // Right
            'btn-x': { left: vw - 150, top: vh - 150 },  // Left
            'btn-y': { left: vw - 100, top: vh - 200 }   // Top
        };
        
        const pos = defaults[groupId];
        if (pos) {
            group.style.left = pos.left + 'px';
            group.style.top = pos.top + 'px';
            group.style.right = 'auto';
            group.style.bottom = 'auto';
        }
    }
    
    resetLayout() {
        this.savedPositions = {};
        this.controlScale = 100;
        this.isSplitMode = false;
        
        // Reset split mode UI
        this.screens.controller.classList.remove('split-mode');
        if (this.elements.splitFaceBtn) {
            this.elements.splitFaceBtn.textContent = 'Split Buttons';
        }
        
        // Reset all group scales to 100%
        document.querySelectorAll('.draggable-group').forEach(group => {
            group.dataset.scale = '100';
            group.style.left = '';
            group.style.top = '';
        });
        
        // Deselect any selected group
        this.deselectGroup();
        
        this.savePositions();
        this.applyPositions();
        this.applyScale();
        
        this.hapticFeedback('heavy');
    }
    
    handleKicked(message) {
        // Show kicked message
        this.showScreen('connect');
        this.showStatus(message || 'Disconnected by server', true);
        this.elements.connectBtn.disabled = false;
        this.isConnected = false;
        
        // Haptic feedback
        this.hapticFeedback('heavy');
        
        // Close WebSocket
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
    }
    
    applyLayout(layout) {
        this.currentLayout = {
            dpad: layout.dpad,
            face: layout.face,
            lstick: layout.lstick,
            rstick: layout.rstick,
            bumpers: layout.bumpers,
            triggers: layout.triggers,
            startback: layout.startback,
            guide: layout.guide
        };
        
        // Control group elements (for inner content)
        const controlGroups = {
            dpad: document.querySelector('.dpad'),
            face: document.querySelector('.face-buttons'),
            lstick: document.getElementById('left-stick-container'),
            rstick: document.getElementById('right-stick-container'),
            bumpers: [document.getElementById('lb'), document.getElementById('rb')],
            triggers: [document.getElementById('lt'), document.getElementById('rt')],
            startback: [document.getElementById('back'), document.getElementById('start')],
            guide: document.getElementById('guide')
        };
        
        // Draggable group elements (for fullscreen mode drag handles)
        const draggableGroups = {
            dpad: document.getElementById('group-dpad'),
            face: document.getElementById('group-face'),
            lstick: document.getElementById('group-lstick'),
            rstick: document.getElementById('group-rstick'),
            top: document.getElementById('group-top'),
            // Individual face buttons
            'btn-a': document.getElementById('group-btn-a'),
            'btn-b': document.getElementById('group-btn-b'),
            'btn-x': document.getElementById('group-btn-x'),
            'btn-y': document.getElementById('group-btn-y')
        };
        
        // Count visible groups for sizing
        let leftCount = 0;
        let rightCount = 0;
        
        if (layout.dpad) leftCount++;
        if (layout.lstick) leftCount++;
        if (layout.face) rightCount++;
        if (layout.rstick) rightCount++;
        
        // Apply visibility to inner content
        Object.entries(controlGroups).forEach(([key, el]) => {
            const show = layout[key];
            if (Array.isArray(el)) {
                el.forEach(e => {
                    if (e) e.classList.toggle('layout-hidden', !show);
                });
            } else if (el) {
                el.classList.toggle('layout-hidden', !show);
            }
        });
        
        // Apply visibility to draggable groups
        if (draggableGroups.dpad) draggableGroups.dpad.classList.toggle('layout-hidden', !layout.dpad);
        if (draggableGroups.face) draggableGroups.face.classList.toggle('layout-hidden', !layout.face);
        if (draggableGroups.lstick) draggableGroups.lstick.classList.toggle('layout-hidden', !layout.lstick);
        if (draggableGroups.rstick) draggableGroups.rstick.classList.toggle('layout-hidden', !layout.rstick);
        
        // Hide top group if nothing in it is visible
        const topVisible = layout.bumpers || layout.triggers || layout.startback || layout.guide;
        if (draggableGroups.top) draggableGroups.top.classList.toggle('layout-hidden', !topVisible);
        
        // Hide individual face buttons if face buttons are hidden
        ['btn-a', 'btn-b', 'btn-x', 'btn-y'].forEach(id => {
            if (draggableGroups[id]) {
                draggableGroups[id].classList.toggle('layout-hidden', !layout.face);
            }
        });
        
        // Apply scaling based on what's visible
        const controller = document.querySelector('.controller');
        
        // Remove all scale classes
        controller.classList.remove('scale-large', 'scale-xl', 'layout-minimal');
        
        // Calculate scale factor
        const totalGroups = leftCount + rightCount;
        
        if (totalGroups <= 2 && !topVisible) {
            controller.classList.add('scale-xl');
        } else if (totalGroups <= 2) {
            controller.classList.add('scale-large');
        } else if (totalGroups <= 3) {
            controller.classList.add('scale-large');
        }
        
        // Hide top row if nothing in it is visible
        const topRow = document.querySelector('.top-row');
        if (topRow) {
            topRow.classList.toggle('layout-hidden', !topVisible);
        }
        
        // Adjust left/right side visibility
        document.querySelector('.left-side')?.classList.toggle('layout-hidden', !layout.dpad && !layout.lstick);
        document.querySelector('.right-side')?.classList.toggle('layout-hidden', !layout.face && !layout.rstick);
        
        // Try to load saved phone settings for this layout
        this.loadPhoneSettingsForLayout();
        
        // Show layout name indicator briefly
        this.showLayoutIndicator(layout.name);
        
        // Haptic feedback
        this.hapticFeedback('medium');
    }
    
    showLayoutIndicator(name) {
        let indicator = document.getElementById('layout-indicator');
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'layout-indicator';
            indicator.className = 'layout-indicator';
            document.body.appendChild(indicator);
        }
        
        indicator.textContent = `Layout: ${name}`;
        indicator.classList.add('show');
        
        setTimeout(() => {
            indicator.classList.remove('show');
        }, 2000);
    }
    
    updateConnectionStatus(connected) {
        this.isConnected = connected;
        
        const status = this.elements.wsStatus;
        if (connected) {
            status.classList.remove('disconnected');
            status.innerHTML = '<span class="dot"></span><span>Connected</span>';
        } else {
            status.classList.add('disconnected');
            status.innerHTML = '<span class="dot"></span><span>Reconnecting...</span>';
        }
        
        // Update fullscreen status
        if (this.elements.fsStatus) {
            this.elements.fsStatus.className = 'fs-status ' + (connected ? 'connected' : 'disconnected');
        }
    }
    
    send(data) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify(data));
        }
    }
    
    sendState() {
        this.send(this.state);
    }
    
    // Button handling
    setupButtons() {
        const buttons = ['a', 'b', 'x', 'y', 'lb', 'rb', 'back', 'start', 'guide', 'up', 'down', 'left', 'right', 'ls', 'rs'];
        
        buttons.forEach(btn => {
            const el = document.getElementById(btn);
            if (!el) return;
            
            this.attachButtonEvents(el, btn);
        });
        
        // Setup split face buttons as well (a-split, b-split, x-split, y-split)
        ['a', 'b', 'x', 'y'].forEach(btn => {
            const splitEl = document.getElementById(btn + '-split');
            if (splitEl) {
                this.attachButtonEvents(splitEl, btn);
            }
        });
    }
    
    attachButtonEvents(el, btn) {
        // Touch events
        el.addEventListener('touchstart', (e) => {
            e.preventDefault();
            this.pressButton(btn, true);
            el.classList.add('active');
        }, { passive: false });
        
        el.addEventListener('touchend', (e) => {
            e.preventDefault();
            this.pressButton(btn, false);
            el.classList.remove('active');
        }, { passive: false });
        
        el.addEventListener('touchcancel', (e) => {
            this.pressButton(btn, false);
            el.classList.remove('active');
        });
        
        // Mouse events (for testing)
        el.addEventListener('mousedown', (e) => {
            e.preventDefault();
            this.pressButton(btn, true);
            el.classList.add('active');
        });
        
        el.addEventListener('mouseup', (e) => {
            this.pressButton(btn, false);
            el.classList.remove('active');
        });
        
        el.addEventListener('mouseleave', (e) => {
            if (this.state[btn]) {
                this.pressButton(btn, false);
                el.classList.remove('active');
            }
        });
    }
    
    pressButton(btn, pressed) {
        this.state[btn] = pressed;
        this.sendState();
        
        // Haptic feedback on press
        if (pressed) {
            this.hapticFeedback('light');
        }
    }
    
    // Analog stick handling
    setupSticks() {
        this.setupStick('left', this.elements.leftStickArea, this.elements.leftStick, this.leftStick);
        this.setupStick('right', this.elements.rightStickArea, this.elements.rightStick, this.rightStick);
        
        // Global touch end handler to catch any missed releases
        document.addEventListener('touchend', (e) => {
            // Check if any active stick's touch is no longer in the touches list
            const activeTouches = new Set();
            for (const touch of e.touches) {
                activeTouches.add(touch.identifier);
            }
            
            // Reset left stick if its touch is gone
            if (this.leftStick.active && !activeTouches.has(this.leftStick.touchId)) {
                this.resetStick('left');
            }
            
            // Reset right stick if its touch is gone
            if (this.rightStick.active && !activeTouches.has(this.rightStick.touchId)) {
                this.resetStick('right');
            }
        });
        
        // Also reset on touchcancel
        document.addEventListener('touchcancel', () => {
            if (this.leftStick.active) this.resetStick('left');
            if (this.rightStick.active) this.resetStick('right');
        });
        
        // Reset sticks when window loses focus (e.g., switching apps)
        window.addEventListener('blur', () => {
            if (this.leftStick.active) this.resetStick('left');
            if (this.rightStick.active) this.resetStick('right');
        });
        
        // Visibility change handler - reset when page becomes hidden
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                if (this.leftStick.active) this.resetStick('left');
                if (this.rightStick.active) this.resetStick('right');
            }
        });
        
        // Drift prevention: check every 200ms if a stick has been "active" too long without updates
        setInterval(() => {
            const now = Date.now();
            const STICK_TIMEOUT = 500; // Reset if no touch update in 500ms
            
            if (this.leftStick.active && (now - this.leftStick.lastUpdate) > STICK_TIMEOUT) {
                console.log('Left stick timeout - resetting');
                this.resetStick('left');
            }
            if (this.rightStick.active && (now - this.rightStick.lastUpdate) > STICK_TIMEOUT) {
                console.log('Right stick timeout - resetting');
                this.resetStick('right');
            }
        }, 200);
    }
    
    resetStick(side) {
        const tracker = side === 'left' ? this.leftStick : this.rightStick;
        const stick = side === 'left' ? this.elements.leftStick : this.elements.rightStick;
        
        tracker.active = false;
        tracker.touchId = null;
        if (stick) stick.style.transform = 'translate(0, 0)';
        
        if (side === 'left') {
            this.state.lx = 0;
            this.state.ly = 0;
        } else {
            this.state.rx = 0;
            this.state.ry = 0;
        }
        this.sendState();
    }
    
    setupStick(side, container, stick, tracker) {
        if (!container || !stick) return;
        
        const maxDistance = 30; // Max pixels from center
        
        const handleStart = (x, y, touchId) => {
            const rect = container.getBoundingClientRect();
            tracker.active = true;
            tracker.startX = rect.left + rect.width / 2;
            tracker.startY = rect.top + rect.height / 2;
            tracker.touchId = touchId;
            tracker.lastUpdate = Date.now();
            
            this.updateStickPosition(side, x, y, tracker, stick, maxDistance);
        };
        
        const handleMove = (x, y) => {
            if (!tracker.active) return;
            tracker.lastUpdate = Date.now();
            this.updateStickPosition(side, x, y, tracker, stick, maxDistance);
        };
        
        const handleEnd = () => {
            tracker.active = false;
            tracker.touchId = null;
            stick.style.transform = 'translate(0, 0)';
            
            if (side === 'left') {
                this.state.lx = 0;
                this.state.ly = 0;
            } else {
                this.state.rx = 0;
                this.state.ry = 0;
            }
            this.sendState();
        };
        
        // Touch events
        container.addEventListener('touchstart', (e) => {
            e.preventDefault();
            const touch = e.changedTouches[0];
            handleStart(touch.clientX, touch.clientY, touch.identifier);
        }, { passive: false });
        
        document.addEventListener('touchmove', (e) => {
            if (!tracker.active) return;
            
            for (const touch of e.changedTouches) {
                if (touch.identifier === tracker.touchId) {
                    e.preventDefault();
                    handleMove(touch.clientX, touch.clientY);
                    break;
                }
            }
        }, { passive: false });
        
        document.addEventListener('touchend', (e) => {
            for (const touch of e.changedTouches) {
                if (touch.identifier === tracker.touchId) {
                    handleEnd();
                    break;
                }
            }
        });
        
        document.addEventListener('touchcancel', (e) => {
            for (const touch of e.changedTouches) {
                if (touch.identifier === tracker.touchId) {
                    handleEnd();
                    break;
                }
            }
        });
        
        // Mouse events (for testing)
        container.addEventListener('mousedown', (e) => {
            e.preventDefault();
            handleStart(e.clientX, e.clientY, -1);
            
            const moveHandler = (e) => handleMove(e.clientX, e.clientY);
            const upHandler = () => {
                handleEnd();
                document.removeEventListener('mousemove', moveHandler);
                document.removeEventListener('mouseup', upHandler);
            };
            
            document.addEventListener('mousemove', moveHandler);
            document.addEventListener('mouseup', upHandler);
        });
    }
    
    updateStickPosition(side, x, y, tracker, stick, maxDistance) {
        let dx = x - tracker.startX;
        let dy = y - tracker.startY;
        
        // Clamp to circle
        const distance = Math.sqrt(dx * dx + dy * dy);
        if (distance > maxDistance) {
            dx = (dx / distance) * maxDistance;
            dy = (dy / distance) * maxDistance;
        }
        
        // Update visual
        stick.style.transform = `translate(${dx}px, ${dy}px)`;
        
        // Update state (-1 to 1)
        const normalizedX = dx / maxDistance;
        const normalizedY = -dy / maxDistance; // Invert Y
        
        if (side === 'left') {
            this.state.lx = normalizedX;
            this.state.ly = normalizedY;
        } else {
            this.state.rx = normalizedX;
            this.state.ry = normalizedY;
        }
        
        this.sendState();
    }
    
    // Trigger handling  
    setupTriggers() {
        this.setupTrigger('lt', document.getElementById('lt'), this.leftTrigger);
        this.setupTrigger('rt', document.getElementById('rt'), this.rightTrigger);
    }
    
    setupTrigger(name, element, tracker) {
        if (!element) return;
        
        const handleStart = (y, touchId) => {
            tracker.active = true;
            tracker.startY = y;
            tracker.touchId = touchId;
            element.classList.add('active');
            this.state[name] = 1;
            this.sendState();
        };
        
        const handleEnd = () => {
            tracker.active = false;
            tracker.touchId = null;
            element.classList.remove('active');
            this.state[name] = 0;
            this.sendState();
        };
        
        // Touch events - triggers are simple press/release for mobile
        element.addEventListener('touchstart', (e) => {
            e.preventDefault();
            const touch = e.changedTouches[0];
            handleStart(touch.clientY, touch.identifier);
            this.hapticFeedback('light');
        }, { passive: false });
        
        document.addEventListener('touchend', (e) => {
            for (const touch of e.changedTouches) {
                if (touch.identifier === tracker.touchId) {
                    handleEnd();
                    break;
                }
            }
        });
        
        document.addEventListener('touchcancel', (e) => {
            for (const touch of e.changedTouches) {
                if (touch.identifier === tracker.touchId) {
                    handleEnd();
                    break;
                }
            }
        });
        
        // Mouse events
        element.addEventListener('mousedown', (e) => {
            e.preventDefault();
            handleStart(e.clientY, -1);
            
            const upHandler = () => {
                handleEnd();
                document.removeEventListener('mouseup', upHandler);
            };
            document.addEventListener('mouseup', upHandler);
        });
    }
}

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    window.controller = new VirtualController();
});
