// Scrolls the keyboard-selected queue row into view.
//
// scrollIntoView is not an option here: under virtualization the selected row
// may not be in the DOM at all. With a fixed row height the position is pure
// arithmetic, so nothing has to be rendered first.
window.queueScroll = {
    toIndex: function (index, rowHeight) {
        const scroller = document.getElementById('app-scroll');
        const list = document.getElementById('queue-list');
        if (!scroller || !list) {
            return;
        }

        // The toolbar is sticky and overlays the top of the scroll area, so a
        // row parked exactly at scrollTop would sit underneath it.
        const toolbar = document.getElementById('queue-toolbar');
        const overlay = toolbar ? toolbar.offsetHeight : 0;

        const top = list.offsetTop + index * rowHeight;
        const bottom = top + rowHeight;

        if (top - overlay < scroller.scrollTop) {
            scroller.scrollTop = top - overlay;
        } else if (bottom > scroller.scrollTop + scroller.clientHeight) {
            scroller.scrollTop = bottom - scroller.clientHeight;
        }
    }
};
