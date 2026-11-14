# Console font fix - how to undo it

**Short version: the console is back on "Microsoft Sans Serif, 8.25pt", the font the
Thetis console was designed and skinned for.  To go back to the .NET 10 default
("Segoe UI, 9pt"), delete one line.**

## The change

`Project Files\Source\Console\console.cs`, in the `Console` constructor, right before
`InitializeComponent()`:

```csharp
            // .NET 10 changed the default font of a control to "Segoe UI, 9pt"; the .NET
            // Framework build of Thetis used "Microsoft Sans Serif, 8.25pt".  The console
            // never sets a form font, so every control that has no font of its own got the
            // bigger one and the text no longer fitted the boxes the designer and the skin
            // were laid out for.  Put the console back on the font it was drawn with.
            // This has to happen BEFORE InitializeComponent(): the controls are then created
            // with the right font and inherit it.  Setting it afterwards fires a layout pass
            // while the console is still half built and the Andromeda button bar throws.
            // See FONT_FIX_HANDOFF.md for how to undo this.
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);

            InitializeComponent();								// Windows Forms Generated Code
```

That is the whole fix - one line (plus the comment).

### Do not move the line below InitializeComponent()

Setting the form font **after** `InitializeComponent()` changes the font of every
child control, and that fires a layout pass while the console is still half
built.  `Console.UpdateButtonBarButtons()` then throws a NullReferenceException
(`Andromeda\Andromeda.cs`, `panelButtonBar_Layout`) and the console dies on
startup with a "Fatal Error" in `ErrorLog.txt`.  This was seen and fixed by
putting the line before `InitializeComponent()` - the controls are then *created*
with the right font and inherit it, and no layout pass is triggered.

## How to revert it

Remove the `this.Font = ...` line and the comment block above it.  Nothing else
depends on it: no other file has to change, nothing in the database has to change,
and no skin file has to change.

```
git revert <the commit>          # if it is still the tip
```

or, by hand, in both trees:

- `SDR-VST3-HL2`   : `Project Files\Source\Console\console.cs`
- `SDR-VST3` (thetisvst) : `Project Files\Source\Console\console.cs`

## Why it was needed

| | .NET Framework 4.8 (the Thetis baseline) | .NET 10 (our port) |
|---|---|---|
| `Control.DefaultFont` | Microsoft Sans Serif, 8.25pt | **Segoe UI, 9pt** |

The console never sets a form font, so every control without a font of its own
inherited the default.  In `console.resx` **350 of the 639 controls** have no
font entry at all - they all got the bigger font.  The designer (and the console
skin) laid the console out for 8.25pt, so the text no longer fitted its boxes.

Measured examples (GDI text metrics at 96dpi):

| control | box | net48 | net10 | effect |
|---|---|---|---|---|
| `labelTS1` "VFO Lock:" | 34x28 | 34x26 | 28x**30** | "Lock:" cut off |
| `lblBandStack` "Band Stack" | 40x28, text bottom right | 35x26 | 30x**30** | text rode up into the kHz box above it |
| `lblTuneStep` "Tune Step:" | 35x33 | 35x26 | 30x**30** | 2px too tall, and it sat 2px too close to the kHz box |

The DPI awareness change made it visible, it did not cause it: the net48 build was
DPI unaware, so Windows bitmap stretched the whole window and text and boxes
always scaled together.  The net10 build is DPI aware (the SDK injects
`<dpiAware>true</dpiAware>`) and renders at the real resolution, so the mismatch
showed up.

## What it does and does not touch

- It only changes the **console form** (and the controls in it that have no font
  of their own).  The 289 controls that *do* have an explicit font in
  `console.resx` keep it, and the LED fonts of the VFO displays are set in code
  and are unaffected.
- **The other forms are not affected** - the setup form, the PS form, the band
  stack form etc. are separate forms with their own font.  They are still on
  Segoe UI 9pt, and the label widenings that were done for them (see
  `LAYOUTCHECK_FIXLOG.md`) stay as they are.  Those widenings are harmless if the
  font is changed there later - the text just gets a little more room.
- `menuStrip1.Font` is the one control in `console.resx` with an explicit
  "Segoe UI, 9pt" - the menu bar keeps that font, as before.
- The skin is unaffected: it is artwork, it does not carry a font.

## If you see something wrong after the change

The fix makes the console text *smaller* again, in the places that had no font of
their own.  If something looks too small or too tight afterwards, that control
needs an explicit font of its own rather than the form font being changed back -
give it "Microsoft Sans Serif, 8.25pt" in the designer, like the rest of the
console, and leave this line alone.
