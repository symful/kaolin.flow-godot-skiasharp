import "Canvas"

globals.once = false

globals.onUpdate = function(delta)
    return true
end function

globals.onRender = function(canvas)
    size = canvas.getSize()
    
    if not globals.once then
        globals.once = true

        print canvas
    end if

    canvas.clearRect 0, 0, size.width, size.height
    canvas.fillStyle = Canvas.randomColor
    canvas.fillRect 0, 0, size.width, size.height
    canvas.fillStyle = "#000"
    canvas.font = "10px Noto Sans";
    canvas.fillText "Hello World!", 0, 0
end function

globals.onResize = function(width, height)
    return true
end function

globals.onReady = function()
    print "Hello World!"

    return true
end function

print "Init"