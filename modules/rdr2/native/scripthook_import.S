/* Establish the normal loader dependency without redistributing the SDK import
   library. Only the documented scriptRegister ABI symbol is referenced. */
.section .data
.p2align 3
.globl hcScriptHookImportAnchor
hcScriptHookImportAnchor:
    .quad "__imp_?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z"
